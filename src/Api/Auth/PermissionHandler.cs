using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// "This endpoint needs members.manage." One requirement per permission; Program.cs registers a
/// policy named after each permission in Permissions.All, so a controller says
/// <c>[Authorize(Policy = Permissions.MembersManage)]</c>.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
  public PermissionRequirement(string permission)
  {
    Permission = permission;
  }

  public string Permission { get; }
}

/// <summary>
/// Our whole authorization check: does the current user have this permission in the current
/// tenant? The answer comes from the session check, which reads it from the database (cached for
/// at most Auth:SessionCheckSeconds), never from the cookie.
/// </summary>
/// <remarks>
/// Not signed in never gets this far: the policy also requires an authenticated user, so that's
/// a 401. Signed in without the permission fails here, and the cookie handler answers 403
/// (AuthCookieEvents.RedirectToAccessDenied).
/// </remarks>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
  private readonly IHttpContextAccessor _httpContextAccessor;

  public PermissionHandler(IHttpContextAccessor httpContextAccessor)
  {
    _httpContextAccessor = httpContextAccessor;
  }

  protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
  {
    HttpContext? httpContext = _httpContextAccessor.HttpContext;
    if (httpContext is null)
    {
      return Task.CompletedTask;
    }

    if (SessionPermissions.Get(httpContext).Contains(requirement.Permission))
    {
      context.Succeed(requirement);
    }

    return Task.CompletedTask;
  }
}
