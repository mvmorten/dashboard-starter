using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using DashboardStarter.Db.Audit;
using DashboardStarter.Db.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace DashboardStarter.Api.Audit;

/// <summary>
/// The current tenant's audit log: who changed what, newest first. Needs audit.view (Admin).
/// One customer's history is on the customer itself (GET /api/customers/{id}/history), for
/// anyone who can see the customer.
/// </summary>
/// <remarks>
/// Read-only, and there is nothing to add here: entries are written by the code that makes each
/// change, never through an endpoint.
/// </remarks>
[ApiController]
[Route("api/audit")]
public sealed class AuditController : ControllerBase
{
  private readonly AuditStore _audit;

  public AuditController(AuditStore audit)
  {
    _audit = audit;
  }

  /// <summary>
  /// One page (25) of the log.
  /// <paramref name="entityType"/>: Customer or Member; none for both.
  /// <paramref name="userId"/>: only what this user did; none for everyone.
  /// </summary>
  /// <remarks>
  /// Like the Customers list: a value it doesn't know is a 400 that names it, never a quiet
  /// fallback. A user id that belongs to nobody here simply matches nothing.
  /// </remarks>
  [HttpGet]
  [Authorize(Policy = Permissions.AuditView)]
  public async Task<ActionResult<AuditListResponse>> List(
    [FromQuery] string? entityType,
    [FromQuery] int? userId,
    [FromQuery] int page = 1,
    CancellationToken cancellationToken = default)
  {
    Dictionary<string, string[]> errors = new();
    if (page < 1)
    {
      errors["page"] = ["Pages start at 1."];
    }

    // An empty value is the same as leaving it out, as the front end's "All" writes it.
    if (string.IsNullOrEmpty(entityType))
    {
      entityType = null;
    }

    if (entityType is not null && !AuditLog.EntityTypes.Contains(entityType, StringComparer.Ordinal))
    {
      errors["entityType"] = [$"Use one of: {string.Join(", ", AuditLog.EntityTypes)}, or leave it out for all."];
    }

    if (userId < 1)
    {
      errors["userId"] = ["Use a user's id, or leave it out for everyone."];
    }

    if (errors.Count > 0)
    {
      return ValidationProblem(new ValidationProblemDetails(errors)
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Some fields need another look",
      });
    }

    int tenantId = AuthClaims.GetTenantId(User) ?? throw new InvalidOperationException("Audit endpoint reached without a current tenant.");
    AuditQuery query = new(entityType, EntityId: null, userId, page);
    return Ok(await _audit.ListAsync(tenantId, query, cancellationToken));
  }
}
