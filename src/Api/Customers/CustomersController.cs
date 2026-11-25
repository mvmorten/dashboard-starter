using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Audit;
using DashboardStarter.Api.Auth;
using DashboardStarter.Api.Demo;
using DashboardStarter.Db.Audit;
using DashboardStarter.Db.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Customers;

/// <summary>
/// The CRUD module's example: the current tenant's customers. Reading needs customers.view;
/// adding, changing, deleting, and restoring need customers.edit, and so does seeing the deleted
/// ones. Always the session's tenant.
/// </summary>
/// <remarks>
/// <para>
/// <b>Edit conflicts.</b> A change or delete sends the RowVersion it loaded. If someone else saved
/// first, the answer is 409 with ProblemDetails carrying <c>rowVersion</c> and <c>current</c> (the
/// customer as it is now), so the client can merge without asking again. 409 means only that;
/// a customer that's gone, or deleted, is a 404.
/// </para>
/// <para>
/// <b>Validation</b> is <see cref="CustomerValidation"/>: 400 with ValidationProblemDetails,
/// errors keyed by field name.
/// </para>
/// <para>
/// <b>The audit log.</b> Every change here writes an entry in the same transaction
/// (<see cref="CustomerStore"/>). A refused request (400, 403, 404, 409, 422) writes none, and
/// neither does a save that changes nothing.
/// </para>
/// </remarks>
[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
  /// <summary>The status filter's value for the deleted customers. Not a status a customer can have.</summary>
  public const string DeletedFilter = "Deleted";

  private readonly CustomerStore _customers;
  private readonly AuditStore _audit;
  private readonly DemoOptions _demoOptions;
  private readonly ILogger<CustomersController> _logger;

  public CustomersController(
    CustomerStore customers, AuditStore audit, IOptions<DemoOptions> demoOptions, ILogger<CustomersController> logger)
  {
    _customers = customers;
    _audit = audit;
    _demoOptions = demoOptions.Value;
    _logger = logger;
  }

  /// <summary>
  /// One page (25) of customers.
  /// <paramref name="search"/>: the start of a name or email.
  /// <paramref name="status"/>: Active or Inactive; none for both. Or Deleted, for the deleted
  /// customers instead of the live ones: that needs customers.edit, and is a 403 without it.
  /// <paramref name="sort"/>: name (the default), email, status, updated or created.
  /// <paramref name="dir"/>: asc (the default) or desc.
  /// </summary>
  /// <remarks>
  /// A value it doesn't know is a 400 that names it, never a quiet fallback to the default: a
  /// link with a typo should say so, not show a different order than it asks for. Values match
  /// exactly, case included, as the front end writes them.
  /// </remarks>
  [HttpGet]
  [Authorize(Policy = Permissions.CustomersView)]
  public async Task<ActionResult<CustomerListResponse>> List(
    [FromQuery] string? search,
    [FromQuery] string? status,
    [FromQuery] string? sort,
    [FromQuery] string? dir,
    [FromQuery] int page = 1,
    CancellationToken cancellationToken = default)
  {
    string? trimmed = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    Dictionary<string, string[]> errors = new();
    if (page < 1)
    {
      errors["page"] = ["Pages start at 1."];
    }

    // Nothing longer can match: it's past the longest name or email.
    if (trimmed is not null && trimmed.Length > CustomerValidation.EmailMaxLength)
    {
      errors["search"] = [$"Use {CustomerValidation.EmailMaxLength} characters or fewer."];
    }

    // An empty value is the same as leaving it out.
    if (string.IsNullOrEmpty(status))
    {
      status = null;
    }

    if (status is not (null or CustomerValidation.Active or CustomerValidation.Inactive or DeletedFilter))
    {
      errors["status"] = [$"Use {CustomerValidation.Active}, {CustomerValidation.Inactive} or {DeletedFilter}, or leave it out for the live ones."];
    }

    sort ??= "name";
    if (!CustomerStore.SortColumns.ContainsKey(sort))
    {
      errors["sort"] = [$"Sort by one of: {string.Join(", ", CustomerStore.SortColumns.Keys)}."];
    }

    dir ??= "asc";
    if (dir is not ("asc" or "desc"))
    {
      errors["dir"] = ["Use asc or desc."];
    }

    if (errors.Count > 0)
    {
      return InvalidFields(errors);
    }

    // Deleted customers are for the people who can restore them. The policy above only asked
    // for customers.view, so this one value checks the other permission itself.
    bool deleted = status == DeletedFilter;
    if (deleted && !CanEdit())
    {
      return Forbid();
    }

    CustomerListQuery query = new(trimmed, deleted ? null : status, deleted, sort, dir == "desc", page);
    return Ok(await _customers.ListAsync(CurrentTenantId(), query, cancellationToken));
  }

  [HttpGet("{id:int}")]
  [Authorize(Policy = Permissions.CustomersView)]
  public async Task<ActionResult<CustomerResponse>> Get(int id, CancellationToken cancellationToken)
  {
    CustomerResponse? customer = await _customers.GetAsync(CurrentTenantId(), id, cancellationToken);
    if (customer is null)
    {
      return CustomerNotFound();
    }

    return Ok(customer);
  }

  /// <summary>
  /// A deleted customer, with who deleted it and when, and the RowVersion a restore sends back.
  /// A live customer isn't here (404): this endpoint is only about deleted ones, so
  /// <see cref="Get"/> can keep saying a deleted customer doesn't exist.
  /// </summary>
  [HttpGet("{id:int}/deleted")]
  [Authorize(Policy = Permissions.CustomersEdit)]
  public async Task<ActionResult<CustomerResponse>> GetDeleted(int id, CancellationToken cancellationToken)
  {
    CustomerResponse? customer = await _customers.GetDeletedAsync(CurrentTenantId(), id, cancellationToken);
    if (customer is null)
    {
      return CustomerNotFound();
    }

    return Ok(customer);
  }

  /// <summary>
  /// The customer's audit entries, newest first, 25 a page. It shows no more than the
  /// customer's own fields and who changed them, so customers.view is enough for a live customer.
  /// </summary>
  /// <remarks>
  /// A deleted customer has a history too (it's what you read before restoring it), but its
  /// entries hold its name, email and notes, and deleted customers are only for people with
  /// customers.edit. Without it, a deleted customer's history is a 404, like the customer.
  /// </remarks>
  [HttpGet("{id:int}/history")]
  [Authorize(Policy = Permissions.CustomersView)]
  public async Task<ActionResult<AuditListResponse>> History(int id, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
  {
    if (page < 1)
    {
      return InvalidFields(new Dictionary<string, string[]> { ["page"] = ["Pages start at 1."] });
    }

    int tenantId = CurrentTenantId();
    bool found = CanEdit()
      ? await _customers.ExistsIncludingDeletedAsync(tenantId, id, cancellationToken)
      : await _customers.GetAsync(tenantId, id, cancellationToken) is not null;
    if (!found)
    {
      return CustomerNotFound();
    }

    AuditQuery query = new(AuditLog.Customer, id, UserId: null, page);
    return Ok(await _audit.ListAsync(tenantId, query, cancellationToken));
  }

  [HttpPost]
  [Authorize(Policy = Permissions.CustomersEdit)]
  public async Task<ActionResult<CustomerResponse>> Create(CustomerFields fields, CancellationToken cancellationToken)
  {
    ValidCustomer? valid = CustomerValidation.Validate(fields, out Dictionary<string, string[]> errors);
    if (valid is null)
    {
      return InvalidFields(errors);
    }

    int tenantId = CurrentTenantId();
    int userId = CurrentUserId();
    CustomerCreated result = await _customers.CreateAsync(tenantId, userId, valid, cancellationToken);
    if (result.CapReached)
    {
      _logger.LogInformation("Refused a new customer in demo tenant {TenantId} for user {UserId}: the tenant is at its cap.", tenantId, userId);
      return Problem(
        statusCode: StatusCodes.Status422UnprocessableEntity,
        title: $"This demo has {_demoOptions.MaxCustomersPerTenant} customers",
        detail: $"A demo tenant holds up to {_demoOptions.MaxCustomersPerTenant} customers, and deleted ones still count. "
          + "Change an existing customer instead, or start a new demo.");
    }

    CustomerResponse customer = result.Customer!;
    _logger.LogInformation("User {UserId} added customer {CustomerId} in tenant {TenantId}.", userId, customer.Id, tenantId);
    return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
  }

  [HttpPut("{id:int}")]
  [Authorize(Policy = Permissions.CustomersEdit)]
  public async Task<ActionResult<CustomerResponse>> Update(int id, UpdateCustomerRequest request, CancellationToken cancellationToken)
  {
    ValidCustomer? valid = CustomerValidation.Validate(request, out Dictionary<string, string[]> errors);
    if (valid is null)
    {
      return InvalidFields(errors);
    }

    int tenantId = CurrentTenantId();
    int userId = CurrentUserId();
    CustomerChange change = await _customers.UpdateAsync(tenantId, userId, id, request.RowVersion, valid, cancellationToken);
    return Answer(change, "changed", id, tenantId, userId, customer => Ok(customer));
  }

  /// <summary>Soft delete: the customer disappears from every list and lookup, and the row stays.</summary>
  [HttpDelete("{id:int}")]
  [Authorize(Policy = Permissions.CustomersEdit)]
  public async Task<IActionResult> Delete(int id, [FromBody] DeleteCustomerRequest request, CancellationToken cancellationToken)
  {
    int tenantId = CurrentTenantId();
    int userId = CurrentUserId();
    CustomerChange change = await _customers.DeleteAsync(tenantId, userId, id, request.RowVersion, cancellationToken);
    return Answer(change, "deleted", id, tenantId, userId, _ => NoContent());
  }

  /// <summary>
  /// Brings a deleted customer back, as it was. 404 if it isn't deleted (or isn't there), so a
  /// second restore is a 404; 409 with the current record if it changed since it was loaded.
  /// </summary>
  [HttpPost("{id:int}/restore")]
  [Authorize(Policy = Permissions.CustomersEdit)]
  public async Task<ActionResult<CustomerResponse>> Restore(int id, RestoreCustomerRequest request, CancellationToken cancellationToken)
  {
    int tenantId = CurrentTenantId();
    int userId = CurrentUserId();
    CustomerChange change = await _customers.RestoreAsync(tenantId, userId, id, request.RowVersion, cancellationToken);
    return Answer(change, "restored", id, tenantId, userId, customer => Ok(customer));
  }

  private ActionResult Answer(
    CustomerChange change, string action, int id, int tenantId, int userId, Func<CustomerResponse?, ActionResult> changed)
  {
    switch (change.Result)
    {
      case CustomerChangeResult.Changed:
        _logger.LogInformation("User {UserId} {Action} customer {CustomerId} in tenant {TenantId}.", userId, action, id, tenantId);
        return changed(change.Customer);
      case CustomerChangeResult.Unchanged:
        // The values the customer already has: nothing was written, and the answer is the
        // customer as it is, with the same RowVersion.
        return changed(change.Customer);
      case CustomerChangeResult.NotFound:
        return CustomerNotFound();
      case CustomerChangeResult.Conflict:
        _logger.LogInformation(
          "Edit conflict: user {UserId} had an old version of customer {CustomerId} in tenant {TenantId}.", userId, id, tenantId);
        return ConflictWithCurrent(change.Customer!);
      default:
        throw new InvalidOperationException($"Unexpected customer change result {change.Result}.");
    }
  }

  private ObjectResult ConflictWithCurrent(CustomerResponse current)
  {
    ObjectResult result = Problem(
      statusCode: StatusCodes.Status409Conflict,
      title: "Someone else changed this customer",
      detail: "It changed since you opened it. Compare your changes with theirs, then save again.");
    if (result.Value is ProblemDetails problem)
    {
      problem.Extensions["rowVersion"] = Convert.ToBase64String(current.RowVersion);
      problem.Extensions["current"] = current;
    }

    return result;
  }

  private ObjectResult CustomerNotFound()
  {
    return Problem(
      statusCode: StatusCodes.Status404NotFound,
      title: "No such customer",
      detail: "It may have been deleted.");
  }

  private ActionResult InvalidFields(Dictionary<string, string[]> errors)
  {
    return ValidationProblem(new ValidationProblemDetails(errors)
    {
      Status = StatusCodes.Status400BadRequest,
      Title = "Some fields need another look",
    });
  }

  private bool CanEdit()
  {
    return SessionPermissions.Get(HttpContext).Contains(Permissions.CustomersEdit);
  }

  // The permission policies only pass with a current tenant (no tenant, no permissions), so
  // every action here has one.
  private int CurrentTenantId()
  {
    return AuthClaims.GetTenantId(User) ?? throw new InvalidOperationException("Customer endpoint reached without a current tenant.");
  }

  private int CurrentUserId()
  {
    return AuthClaims.GetUserId(User) ?? throw new InvalidOperationException("Signed-in user without a UserId claim.");
  }
}
