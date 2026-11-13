using System;

namespace DashboardStarter.Db.Passwords;

/// <summary>
/// What a new password must be, following current NIST guidance (SP 800-63B): a length range,
/// and not the account's own email. No rules about digits or symbols, which mostly produce
/// "Password1!". One place for the API (reset and change password) and the Db runner
/// (create-user, set-password), so the rules can't drift apart.
/// </summary>
public static class PasswordRules
{
  public const int MinimumLength = 12;

  // Long passphrases are welcome; the cap only bounds the work one hash does.
  public const int MaximumLength = 128;

  /// <summary>Returns null if the password is acceptable for this account, otherwise why not.</summary>
  public static string? Check(string password, string email)
  {
    string? lengthProblem = CheckLength(password);
    if (lengthProblem is not null)
    {
      return lengthProblem;
    }

    // The email is the first thing anyone guessing would try.
    if (string.Equals(password.Trim(), email.Trim(), StringComparison.OrdinalIgnoreCase))
    {
      return "The password can't be your email address.";
    }

    return null;
  }

  /// <summary>
  /// The length rule alone, for when the account isn't known yet (a reset link, before its
  /// token is looked up). <see cref="Check"/> still has to run once the email is known.
  /// </summary>
  public static string? CheckLength(string password)
  {
    if (password.Length < MinimumLength)
    {
      return $"The password must be at least {MinimumLength} characters.";
    }

    if (password.Length > MaximumLength)
    {
      return $"The password can be at most {MaximumLength} characters.";
    }

    return null;
  }
}
