using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Which build of the app this is: the CI run number, the commit it was built from, and when.
/// The front end in wwwroot is part of the same build, so this one number covers both.
/// </summary>
/// <remarks>
/// <para>
/// The deploy pipeline stamps these into the assembly at publish time
/// (<c>dotnet publish -p:BuildNumber=... -p:BuildCommit=... -p:BuildTime=...</c>; see
/// DashboardStarter.Api.csproj), so they describe the code that's actually running, not a
/// setting someone could change on the server afterwards.
/// </para>
/// <para>
/// All three are null in a local build. They're safe to show publicly: the repo is public,
/// so a commit hash tells nobody anything they couldn't already read.
/// </para>
/// </remarks>
/// <param name="Number">The CI run number, such as "57".</param>
/// <param name="Commit">The full commit hash the build came from.</param>
/// <param name="BuiltAt">When the build ran, in UTC.</param>
public sealed record BuildInfo(string? Number, string? Commit, DateTime? BuiltAt)
{
  public static BuildInfo Current { get; } = Read();

  private static BuildInfo Read()
  {
    AssemblyMetadataAttribute[] metadata = typeof(BuildInfo).Assembly
      .GetCustomAttributes<AssemblyMetadataAttribute>()
      .ToArray();

    string? number = Find(metadata, "BuildNumber");
    string? commit = Find(metadata, "BuildCommit");
    DateTime? builtAt = ParseUtc(Find(metadata, "BuildTime"));

    return new BuildInfo(number, commit, builtAt);
  }

  private static string? Find(AssemblyMetadataAttribute[] metadata, string key)
  {
    string? value = metadata.FirstOrDefault(attribute => attribute.Key == key)?.Value;
    if (string.IsNullOrWhiteSpace(value))
    {
      return null;
    }

    return value;
  }

  private static DateTime? ParseUtc(string? value)
  {
    if (value is null)
    {
      return null;
    }

    bool parsed = DateTime.TryParse(
      value,
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
      out DateTime builtAt);
    if (!parsed)
    {
      return null;
    }

    return builtAt;
  }
}
