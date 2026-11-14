using System.Collections.Generic;

namespace DashboardStarter.Api.Customers;

/// <summary>
/// The rules for a customer's fields, checked on the server for every create and update. The
/// lengths are the column lengths in sql/migrations/0013_customers.sql.
/// </summary>
/// <remarks>
/// <para>
/// Text is trimmed, and an optional field that's empty after trimming is stored as NULL, so
/// "no email" is one value in the database, not two.
/// </para>
/// <para>
/// Errors are keyed by the JSON field name ("name", "email"), so the form can show each one
/// next to its field. The forms step brings shared rules for the front end; until then the
/// server is the only check.
/// </para>
/// </remarks>
public static class CustomerValidation
{
  public const int NameMaxLength = 200;
  public const int EmailMaxLength = 320;
  public const int PhoneMaxLength = 50;
  public const int NotesMaxLength = 2000;

  public const string Active = "Active";
  public const string Inactive = "Inactive";

  /// <summary>Returns the cleaned-up fields, or null and the errors.</summary>
  public static ValidCustomer? Validate(CustomerFields fields, out Dictionary<string, string[]> errors)
  {
    errors = new Dictionary<string, string[]>();

    string? name = Clean(fields.Name);
    if (name is null)
    {
      errors["name"] = ["Enter a name."];
    }
    else if (name.Length > NameMaxLength)
    {
      errors["name"] = [$"Use {NameMaxLength} characters or fewer."];
    }

    string? email = Clean(fields.Email);
    if (email is not null && email.Length > EmailMaxLength)
    {
      errors["email"] = [$"Use {EmailMaxLength} characters or fewer."];
    }
    else if (email is not null && !LooksLikeEmail(email))
    {
      errors["email"] = ["Enter an email address like name@example.com, or leave it empty."];
    }

    string? phone = Clean(fields.Phone);
    if (phone is not null && phone.Length > PhoneMaxLength)
    {
      errors["phone"] = [$"Use {PhoneMaxLength} characters or fewer."];
    }

    string? notes = Clean(fields.Notes);
    if (notes is not null && notes.Length > NotesMaxLength)
    {
      errors["notes"] = [$"Use {NotesMaxLength} characters or fewer."];
    }

    string? status = Clean(fields.Status);
    if (status is not (Active or Inactive))
    {
      errors["status"] = [$"Choose {Active} or {Inactive}."];
    }

    if (errors.Count > 0)
    {
      return null;
    }

    return new ValidCustomer(name!, email, phone, status!, notes);
  }

  private static string? Clean(string? value)
  {
    string? trimmed = value?.Trim();
    return string.IsNullOrEmpty(trimmed) ? null : trimmed;
  }

  // Deliberately loose: one @ with something on each side, no spaces. Whether an address really
  // works is only known by sending to it, and customers aren't emailed yet.
  private static bool LooksLikeEmail(string email)
  {
    int at = email.IndexOf('@');
    return at > 0
      && at == email.LastIndexOf('@')
      && at < email.Length - 1
      && !email.Contains(' ');
  }
}

/// <summary>A customer's fields after <see cref="CustomerValidation"/>: trimmed, empty optional fields as null.</summary>
public sealed record ValidCustomer(string Name, string? Email, string? Phone, string Status, string? Notes);
