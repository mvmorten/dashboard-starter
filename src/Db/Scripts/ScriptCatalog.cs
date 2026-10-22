using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace DashboardStarter.Db.Scripts;

/// <summary>
/// The SQL scripts compiled into this assembly from /sql (see DashboardStarter.Db.csproj).
/// Each list is sorted by file name, which is the order the scripts run in.
/// </summary>
public static class ScriptCatalog
{
  public static IReadOnlyList<SqlScript> Migrations { get; } = Load("migrations");

  public static IReadOnlyList<SqlScript> Programmable { get; } = Load("programmable");

  public static IReadOnlyList<SqlScript> Seed { get; } = Load("seed");

  private static List<SqlScript> Load(string folder)
  {
    Assembly assembly = typeof(ScriptCatalog).Assembly;
    string prefix = "sql/" + folder + "/";
    List<SqlScript> scripts = new();

    foreach (string resourceName in assembly.GetManifestResourceNames())
    {
      // MSBuild uses the OS path separator in resource names, so normalize to "/".
      string normalizedName = resourceName.Replace('\\', '/');
      if (!normalizedName.StartsWith(prefix, StringComparison.Ordinal))
      {
        continue;
      }

      string text = ReadText(assembly, resourceName);
      string name = normalizedName.Substring(prefix.Length);
      scripts.Add(new SqlScript(name, text, ComputeHash(text)));
    }

    return scripts.OrderBy(script => script.Name, StringComparer.Ordinal).ToList();
  }

  private static string ReadText(Assembly assembly, string resourceName)
  {
    using Stream stream = assembly.GetManifestResourceStream(resourceName)
      ?? throw new InvalidOperationException($"Embedded script '{resourceName}' could not be read.");
    using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

    // Normalize line endings so a checkout with CRLF (Windows) and one with LF
    // (Mac, Linux, CI) produce the same hash for the same script.
    return reader.ReadToEnd().Replace("\r\n", "\n");
  }

  private static string ComputeHash(string text)
  {
    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
    return Convert.ToHexStringLower(hash);
  }
}
