using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using DashboardStarter.Api.Database;
using DashboardStarter.Db.Authorization;
using DashboardStarter.Db.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Members;

/// <summary>
/// The Members page: who is in the current tenant, and (with members.manage) changing their role
/// or disabling their access. Always the session's tenant: no tenant id comes from the browser.
/// </summary>
/// <remarks>
/// The rules, all checked here and not only in the UI:
/// <list type="bullet">
/// <item>You can't change your own role or disable yourself.</item>
/// <item>The shared demo account can't change members (it can look).</item>
/// <item>A tenant never loses its last active member who can manage members, even when two
/// admins demote each other at once (<see cref="Memberships"/>).</item>
/// </list>
/// There's no adding or removing members yet: that's invites, a later step.
/// </remarks>
[ApiController]
[Route("api/members")]
public sealed class MembersController : ControllerBase
{
  private readonly MemberStore _memberStore;
  private readonly UserStore _userStore;
  private readonly SqlConnectionFactory _connectionFactory;
  private readonly IMemoryCache _cache;
  private readonly ILogger<MembersController> _logger;

  public MembersController(
    MemberStore memberStore,
    UserStore userStore,
    SqlConnectionFactory connectionFactory,
    IMemoryCache cache,
    ILogger<MembersController> logger)
  {
    _memberStore = memberStore;
    _userStore = userStore;
    _connectionFactory = connectionFactory;
    _cache = cache;
    _logger = logger;
  }

  [HttpGet]
  [Authorize(Policy = Permissions.MembersView)]
  public async Task<ActionResult<MembersResponse>> List(CancellationToken cancellationToken)
  {
    int tenantId = CurrentTenantId();
    IReadOnlyList<MemberResponse> members = await _memberStore.GetMembersAsync(tenantId, cancellationToken);
    IReadOnlyList<RoleResponse> roles = await _memberStore.GetRolesAsync(cancellationToken);
    return Ok(new MembersResponse(members, roles));
  }

  [HttpPut("{userId:int}/role")]
  [Authorize(Policy = Permissions.MembersManage)]
  public Task<ActionResult<MemberChangedResponse>> ChangeRole(int userId, ChangeRoleRequest request, CancellationToken cancellationToken)
  {
    return ChangeAsync(
      userId,
      "change your own role",
      $"role {request.RoleId}",
      (connection, transaction, tenantId, actorId) => Memberships.SetRoleAsync(
        connection, transaction, tenantId, userId, request.RoleId, request.RowVersion, actorId),
      cancellationToken);
  }

  [HttpPut("{userId:int}/status")]
  [Authorize(Policy = Permissions.MembersManage)]
  public Task<ActionResult<MemberChangedResponse>> ChangeStatus(int userId, ChangeStatusRequest request, CancellationToken cancellationToken)
  {
    bool active = request.Status == Memberships.Active;
    return ChangeAsync(
      userId,
      "disable yourself",
      "status " + request.Status,
      (connection, transaction, tenantId, actorId) => Memberships.SetStatusAsync(
        connection, transaction, tenantId, userId, active, request.RowVersion, actorId),
      cancellationToken);
  }

  private async Task<ActionResult<MemberChangedResponse>> ChangeAsync(
    int userId,
    string selfChange,
    string changeDescription,
    Func<SqlConnection, SqlTransaction, int, int, Task<MembershipChange>> change,
    CancellationToken cancellationToken)
  {
    int tenantId = CurrentTenantId();
    int actorId = AuthClaims.GetUserId(User) ?? throw new InvalidOperationException("Signed-in user without a UserId claim.");

    if (userId == actorId)
    {
      _logger.LogInformation("Refused {Change} for user {ActorId}: it's their own membership.", changeDescription, actorId);
      return Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "That's you",
        detail: $"You can't {selfChange}. Ask another admin of this tenant.");
    }

    // Like changing its password: everyone shares the demo account, so it can look but not change.
    UserSummary? actor = await _userStore.GetUserAsync(actorId, cancellationToken);
    if (actor is null || actor.IsDemo)
    {
      _logger.LogInformation("Refused {Change} for user {MemberId} by the demo account {ActorId}.", changeDescription, userId, actorId);
      return Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "The demo account can't change members",
        detail: "Everyone shares the demo account, so its member changes are turned off. The controls show what an admin sees.");
    }

    await using SqlConnection connection = await _connectionFactory.OpenAsync(cancellationToken);
    await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
    MembershipChange result = await change(connection, transaction, tenantId, actorId);
    if (result.Result != MembershipChangeResult.Changed)
    {
      _logger.LogInformation(
        "Refused {Change} for user {MemberId} in tenant {TenantId} by user {ActorId}: {Result}.",
        changeDescription,
        userId,
        tenantId,
        actorId,
        result.Result);
    }

    switch (result.Result)
    {
      case MembershipChangeResult.Changed:
        await transaction.CommitAsync(cancellationToken);
        break;
      case MembershipChangeResult.NotFound:
        return Problem(statusCode: StatusCodes.Status404NotFound, title: "Not a member of this tenant");
      case MembershipChangeResult.UnknownRole:
        return Problem(statusCode: StatusCodes.Status400BadRequest, title: "No such role");
      case MembershipChangeResult.ActorCannotManage:
        return Problem(
          statusCode: StatusCodes.Status403Forbidden,
          title: "You don't have access to this",
          detail: "Your role in this tenant no longer includes managing members.");
      case MembershipChangeResult.LastManager:
        return Problem(
          statusCode: StatusCodes.Status409Conflict,
          title: "The tenant needs an admin",
          detail: "That would leave nobody who can manage members. Give someone else that role first.");
      case MembershipChangeResult.Conflict:
        return ConflictWithCurrentVersion(result.RowVersion!);
      default:
        throw new InvalidOperationException($"Unexpected membership change result {result.Result}.");
    }

    // Their next request reads the new role or status, like after a password change. Within
    // Auth:SessionCheckSeconds at most: other instances keep their entries until they expire,
    // and a request of theirs already reading the old state can put it back for one window.
    // What they can manage is checked again under the lock (Memberships), so that window
    // never lets anyone change members.
    SessionCache.Forget(_cache, userId, [tenantId]);
    _logger.LogInformation(
      "User {ActorId} set {Change} for user {MemberId} in tenant {TenantId}.", actorId, changeDescription, userId, tenantId);

    return Ok(new MemberChangedResponse(result.RowVersion!));
  }

  private ObjectResult ConflictWithCurrentVersion(byte[] currentRowVersion)
  {
    ObjectResult result = Problem(
      statusCode: StatusCodes.Status409Conflict,
      title: "Someone else changed this member",
      detail: "Their role or status changed since you loaded the page. Check it and try again.");
    if (result.Value is ProblemDetails problem)
    {
      problem.Extensions["rowVersion"] = Convert.ToBase64String(currentRowVersion);
    }

    return result;
  }

  // The permission policies only pass with a current tenant (no tenant, no permissions), so
  // every action here has one.
  private int CurrentTenantId()
  {
    return AuthClaims.GetTenantId(User) ?? throw new InvalidOperationException("Member endpoint reached without a current tenant.");
  }
}
