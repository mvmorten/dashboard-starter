namespace DashboardStarter.Db.Passwords;

/// <summary>
/// Turns a password into a storable hash and checks a password against one.
/// Our own small interface, not ASP.NET Core Identity's.
/// </summary>
/// <remarks>
/// Lives in the Db project because both the API (sign-in) and the Db runner (create-user,
/// set-password, demo) need it, and the API already references this project for its list of
/// migrations. The runner never depends on the API.
/// </remarks>
public interface IPasswordHasher
{
  /// <summary>Hashes with a new random salt and the current settings.</summary>
  string Hash(string password);

  /// <summary>Checks a password against a stored hash.</summary>
  PasswordVerification Verify(string password, string storedHash);
}

public enum PasswordVerification
{
  /// <summary>Wrong password, or a stored hash we can't read.</summary>
  Failed,

  /// <summary>Correct password, stored with the current settings.</summary>
  Success,

  /// <summary>Correct password, but stored with weaker settings than the current ones: hash it again and save.</summary>
  SuccessRehashNeeded,
}
