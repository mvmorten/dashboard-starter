using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DashboardStarter.Api.Auth;
using DashboardStarter.Api.Demo;
using DashboardStarter.Db.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Customers;

/// <summary>
/// The CRUD module's example: the current tenant's customers. Reading needs customers.view;
/// adding, changing, and deleting need customers.edit. Always the session's tenant.
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
/// </remarks>
[ApiController]
[Route("api/customers")]
public sealed class CustomersController : ControllerBase
{
  private readonly CustomerStore _customers;
  private readonly DemoOptions _demoOptions;
  private readonly ILogger<CustomersController> _logger;

  public CustomersController(CustomerStore customers, IOptions<DemoOptions> demoOptions, ILogger<CustomersController> logger)
  {
    _customers = customers;
    _demoOptions = demoOptions.Value;
    _logger = logger;
  }

  /// <summary>One page (25) of customers, sorted by name. <paramref name="search"/>: the start of a name or email.</summary>
  [HttpGet]
  [Authorize(Policy = Permissions.CustomersView)]
  public async Task<ActionResult<CustomerListResponse>> List(
    [FromQuery] string? search, [FromQuery] int page = 1, CancellationToken cancellationToken = default)
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

    if (errors.Count > 0)
    {
      return InvalidFields(errors);
    }

    return Ok(await _customers.ListAsync(CurrentTenantId(), trimmed, page, cancellationToken));
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

  private ActionResult Answer(
    CustomerChange change, string action, int id, int tenantId, int userId, Func<CustomerResponse?, ActionResult> changed)
  {
    switch (change.Result)
    {
      case CustomerChangeResult.Changed:
        _logger.LogInformation("User {UserId} {Action} customer {CustomerId} in tenant {TenantId}.", userId, action, id, tenantId);
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
