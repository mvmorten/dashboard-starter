using DashboardStarter.Db.Audit;

namespace DashboardStarter.Api.Customers;

/// <summary>
/// Which customer fields the audit log records: these five, the ones a person edits, and no
/// others. A column added to Customers later isn't logged until someone adds a line here.
/// The names are the API's field names, so the front end labels them like the form does.
/// </summary>
internal static class CustomerAudit
{
  /// <summary>A new customer: every field that has a value, with nothing before it.</summary>
  public static AuditChanges Created(ValidCustomer customer)
  {
    return new AuditChanges()
      .Add("name", null, customer.Name)
      .Add("email", null, customer.Email)
      .Add("phone", null, customer.Phone)
      .Add("status", null, customer.Status)
      .Add("notes", null, customer.Notes);
  }

  /// <summary>The fields that differ between the stored customer and the save. Empty: the save changes nothing.</summary>
  public static AuditChanges Updated(CustomerResponse before, ValidCustomer after)
  {
    return new AuditChanges()
      .Add("name", before.Name, after.Name)
      .Add("email", before.Email, after.Email)
      .Add("phone", before.Phone, after.Phone)
      .Add("status", before.Status, after.Status)
      .Add("notes", before.Notes, after.Notes);
  }
}
