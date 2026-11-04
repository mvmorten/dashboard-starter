using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace DashboardStarter.Db.Passwords;

/// <summary>
/// Argon2id password hashes, stored as PHC strings:
/// <c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// .NET has no built-in Argon2, so the algorithm itself comes from the
/// Konscious.Security.Cryptography.Argon2 package. Everything around it is here: the random
/// salt, the PHC format, the constant-time comparison, and deciding when to rehash.
/// </para>
/// <para>
/// PHC is the format other Argon2 libraries read and write (salt and hash in base64 without
/// padding), so these hashes can move to another library or language later.
/// </para>
/// </remarks>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
  private const int SaltBytes = 16;
  private const int HashBytes = 32;

  // Argon2 version 1.3, the current one. Konscious only implements this version.
  private const int Version = 19;

  private readonly PasswordHashingOptions _options;

  public Argon2idPasswordHasher(PasswordHashingOptions options)
  {
    options.Validate();
    _options = options;
  }

  public string Hash(string password)
  {
    byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
    byte[] hash = Compute(password, salt, _options.MemorySizeKiB, _options.Iterations, _options.Parallelism, HashBytes);

    return string.Create(
      CultureInfo.InvariantCulture,
      $"$argon2id$v={Version}$m={_options.MemorySizeKiB},t={_options.Iterations},p={_options.Parallelism}${ToBase64(salt)}${ToBase64(hash)}");
  }

  public PasswordVerification Verify(string password, string storedHash)
  {
    if (!TryParse(storedHash, out StoredHash stored))
    {
      return PasswordVerification.Failed;
    }

    byte[] actual = Compute(password, stored.Salt, stored.MemorySizeKiB, stored.Iterations, stored.Parallelism, stored.Hash.Length);

    // Constant time: how long the comparison takes says nothing about how many bytes matched.
    if (!CryptographicOperations.FixedTimeEquals(actual, stored.Hash))
    {
      return PasswordVerification.Failed;
    }

    bool weaker = stored.MemorySizeKiB < _options.MemorySizeKiB
      || stored.Iterations < _options.Iterations
      || stored.Parallelism < _options.Parallelism
      || stored.Hash.Length < HashBytes;

    return weaker ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Success;
  }

  private static byte[] Compute(string password, byte[] salt, int memorySizeKiB, int iterations, int parallelism, int length)
  {
    byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
    try
    {
      using Argon2id argon2 = new(passwordBytes)
      {
        Salt = salt,
        MemorySize = memorySizeKiB,
        Iterations = iterations,
        DegreeOfParallelism = parallelism,
      };

      return argon2.GetBytes(length);
    }
    finally
    {
      // Don't leave the password lying around in memory longer than needed.
      CryptographicOperations.ZeroMemory(passwordBytes);
    }
  }

  // $argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>, split on "$" gives
  // ["", "argon2id", "v=19", "m=19456,t=2,p=1", "<salt>", "<hash>"].
  private static bool TryParse(string value, out StoredHash stored)
  {
    stored = default;
    string[] parts = value.Split('$');
    if (parts.Length != 6 || parts[0].Length != 0 || parts[1] != "argon2id" || parts[2] != "v=" + Version)
    {
      return false;
    }

    string[] settings = parts[3].Split(',');
    if (settings.Length != 3
      || !TryReadSetting(settings[0], "m=", out int memorySizeKiB)
      || !TryReadSetting(settings[1], "t=", out int iterations)
      || !TryReadSetting(settings[2], "p=", out int parallelism))
    {
      return false;
    }

    // Refuse settings that would make one verification absurdly expensive. A hash in the
    // database should never look like this, but if one does, it shouldn't cost 4 GB of memory.
    if (memorySizeKiB > 4 * 1024 * 1024 || iterations > 100 || parallelism > 64 || memorySizeKiB < 8 * parallelism)
    {
      return false;
    }

    if (!TryFromBase64(parts[4], out byte[] salt) || !TryFromBase64(parts[5], out byte[] hash))
    {
      return false;
    }

    if (salt.Length < 8 || hash.Length < 16)
    {
      return false;
    }

    stored = new StoredHash(memorySizeKiB, iterations, parallelism, salt, hash);
    return true;
  }

  private static bool TryReadSetting(string text, string prefix, out int value)
  {
    value = 0;
    return text.StartsWith(prefix, StringComparison.Ordinal)
      && int.TryParse(text.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out value)
      && value > 0;
  }

  // PHC strings use standard base64 without the trailing "=" padding.
  private static string ToBase64(byte[] bytes)
  {
    return Convert.ToBase64String(bytes).TrimEnd('=');
  }

  private static bool TryFromBase64(string text, out byte[] bytes)
  {
    bytes = [];
    int padding = (4 - (text.Length % 4)) % 4;
    if (padding == 3)
    {
      return false;
    }

    try
    {
      bytes = Convert.FromBase64String(text + new string('=', padding));
      return true;
    }
    catch (FormatException)
    {
      return false;
    }
  }

  private readonly record struct StoredHash(int MemorySizeKiB, int Iterations, int Parallelism, byte[] Salt, byte[] Hash);
}
