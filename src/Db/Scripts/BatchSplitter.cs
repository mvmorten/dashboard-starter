using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace DashboardStarter.Db.Scripts;

/// <summary>
/// Splits a script into batches on lines that contain only "GO", the way SSMS and sqlcmd do.
/// GO is not T-SQL; it's a client-side separator, so SQL Server never sees it.
/// </summary>
/// <remarks>
/// Kept deliberately simple: a GO line inside a block comment or a multi-line string
/// would also split the script, and "GO 5" (repeat count) isn't supported.
/// </remarks>
public static partial class BatchSplitter
{
  public static IReadOnlyList<string> Split(string script)
  {
    List<string> batches = new();
    StringBuilder current = new();

    foreach (string line in script.Split('\n'))
    {
      if (GoLine().IsMatch(line))
      {
        AddIfNotEmpty(batches, current);
        continue;
      }

      current.Append(line).Append('\n');
    }

    AddIfNotEmpty(batches, current);
    return batches;
  }

  private static void AddIfNotEmpty(List<string> batches, StringBuilder current)
  {
    string batch = current.ToString();
    current.Clear();
    if (string.IsNullOrWhiteSpace(batch))
    {
      return;
    }

    batches.Add(batch);
  }

  // "GO" alone on a line, any case, optionally followed by a -- comment.
  [GeneratedRegex(@"^\s*GO\s*(--.*)?$", RegexOptions.IgnoreCase)]
  private static partial Regex GoLine();
}
