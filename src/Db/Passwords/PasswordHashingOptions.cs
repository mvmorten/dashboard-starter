using System;
using System.Globalization;

namespace DashboardStarter.Db.Passwords;

/// <summary>
/// Argon2id settings for new hashes. The defaults are OWASP's first recommendation:
/// 19 MiB of memory, 2 passes, 1 lane.
/// </summary>
/// <remarks>
/// The API reads these from the <c>PasswordHashing</c> configuration section, and the Db runner
/// from the same names as environment variables (<c>PasswordHashing__Iterations</c>), so both
/// hash with the same settings. Raising them later is safe: stored hashes carry their own
/// settings, and a user is rehashed with the new ones on their next sign-in.
/// </remarks>
public sealed class PasswordHashingOptions
{
  public const string SectionName = "PasswordHashing";

  public int MemorySizeKiB { get; set; } = 19456;

  public int Iterations { get; set; } = 2;

  public int Parallelism { get; set; } = 1;

  public static PasswordHashingOptions FromEnvironment()
  {
    PasswordHashingOptions options = new();
    options.MemorySizeKiB = ReadInt("MemorySizeKiB", options.MemorySizeKiB);
    options.Iterations = ReadInt("Iterations", options.Iterations);
    options.Parallelism = ReadInt("Parallelism", options.Parallelism);
    return options;
  }

  /// <summary>Refuses settings too weak or too odd to be a mistake worth keeping.</summary>
  public void Validate()
  {
    if (MemorySizeKiB < 8 * Parallelism || Iterations < 1 || Parallelism < 1)
    {
      throw new InvalidOperationException(
        "PasswordHashing settings are invalid: Iterations and Parallelism must be at least 1, and MemorySizeKiB at least 8 per lane.");
    }
  }

  private static int ReadInt(string name, int defaultValue)
  {
    string? value = Environment.GetEnvironmentVariable(SectionName + "__" + name);
    if (string.IsNullOrWhiteSpace(value))
    {
      return defaultValue;
    }

    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
    {
      throw new InvalidOperationException($"{SectionName}__{name} must be a whole number.");
    }

    return parsed;
  }
}
