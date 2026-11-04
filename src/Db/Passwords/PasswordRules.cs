namespace DashboardStarter.Db.Passwords;

/// <summary>
/// What a new password must be. Length only, following current NIST guidance (SP 800-63B):
/// no rules about digits or symbols, which mostly produce "Password1!".
/// </summary>
public static class PasswordRules
{
  public const int MinimumLength = 12;

  // Long passphrases are welcome; the cap only bounds the work one hash does.
  public const int MaximumLength = 128;

  /// <summary>Returns null if the password is acceptable, otherwise why not.</summary>
  public static string? Check(string password)
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
