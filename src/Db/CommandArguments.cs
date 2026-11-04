using System;
using System.Collections.Generic;

namespace DashboardStarter.Db;

/// <summary>
/// The options after the command name: <c>--email ann@example.com --password-stdin</c>.
/// Each command says which options it accepts; anything else is an error, so a typo
/// never gets silently ignored.
/// </summary>
internal sealed class CommandArguments
{
  private readonly Dictionary<string, string> _values;
  private readonly HashSet<string> _flags;

  private CommandArguments(Dictionary<string, string> values, HashSet<string> flags)
  {
    _values = values;
    _flags = flags;
  }

  /// <param name="args">Everything after the command name.</param>
  /// <param name="valueOptions">Options followed by a value, such as "email".</param>
  /// <param name="flagOptions">Options on their own, such as "password-stdin".</param>
  public static CommandArguments Parse(string[] args, string[] valueOptions, string[] flagOptions)
  {
    Dictionary<string, string> values = new(StringComparer.Ordinal);
    HashSet<string> flags = new(StringComparer.Ordinal);

    for (int index = 0; index < args.Length; index++)
    {
      string arg = args[index];
      if (!arg.StartsWith("--", StringComparison.Ordinal))
      {
        throw new RunnerException($"Unexpected argument '{arg}'.");
      }

      string name = arg.Substring(2);
      if (name == "password")
      {
        throw new RunnerException(
          "Passwords are never taken as arguments: they end up in shell history and process lists. "
            + "Leave it out to be prompted, or pass --password-stdin and pipe it in.");
      }

      if (Array.IndexOf(flagOptions, name) >= 0)
      {
        flags.Add(name);
        continue;
      }

      if (Array.IndexOf(valueOptions, name) < 0)
      {
        throw new RunnerException($"Unknown option '{arg}'.");
      }

      if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
      {
        throw new RunnerException($"Option '{arg}' needs a value.");
      }

      values[name] = args[index + 1];
      index++;
    }

    return new CommandArguments(values, flags);
  }

  public string? Get(string name)
  {
    return _values.TryGetValue(name, out string? value) ? value : null;
  }

  public string GetRequired(string name)
  {
    string? value = Get(name);
    if (string.IsNullOrWhiteSpace(value))
    {
      throw new RunnerException($"--{name} is required.");
    }

    return value.Trim();
  }

  public bool Has(string flag)
  {
    return _flags.Contains(flag);
  }
}
