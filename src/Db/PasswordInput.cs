using System;
using System.Text;
using DashboardStarter.Db.Passwords;

namespace DashboardStarter.Db;

/// <summary>
/// Reads a new password for create-user and set-password: typed at a hidden prompt, or one
/// line from standard input with <c>--password-stdin</c> (for scripts and CI). Never from a
/// command-line argument.
/// </summary>
internal static class PasswordInput
{
  public static string Read(bool fromStdin)
  {
    string password = fromStdin ? ReadFromStdin() : ReadFromPrompt();

    string? problem = PasswordRules.Check(password);
    if (problem is not null)
    {
      throw new RunnerException(problem);
    }

    return password;
  }

  private static string ReadFromStdin()
  {
    string? line = Console.In.ReadLine();
    if (line is null)
    {
      throw new RunnerException("--password-stdin was given, but standard input was empty.");
    }

    return line;
  }

  private static string ReadFromPrompt()
  {
    if (Console.IsInputRedirected)
    {
      throw new RunnerException("Standard input isn't a terminal, so there's nobody to prompt. Pipe the password in with --password-stdin.");
    }

    string first = Prompt("New password: ");
    string second = Prompt("Type it again: ");
    if (!string.Equals(first, second, StringComparison.Ordinal))
    {
      throw new RunnerException("The two passwords don't match. Nothing was changed.");
    }

    return first;
  }

  // Reads keys without echoing them, so the password never appears on screen.
  private static string Prompt(string label)
  {
    Console.Write(label);
    StringBuilder password = new();

    while (true)
    {
      ConsoleKeyInfo key = Console.ReadKey(intercept: true);
      if (key.Key == ConsoleKey.Enter)
      {
        break;
      }

      if (key.Key == ConsoleKey.Backspace)
      {
        if (password.Length > 0)
        {
          password.Length--;
        }

        continue;
      }

      if (!char.IsControl(key.KeyChar))
      {
        password.Append(key.KeyChar);
      }
    }

    Console.WriteLine();
    return password.ToString();
  }
}
