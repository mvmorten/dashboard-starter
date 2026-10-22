using System;

namespace DashboardStarter.Db;

/// <summary>
/// An expected failure with a message meant for the person running the command.
/// Program prints the message and exits with code 1, without a stack trace.
/// </summary>
internal sealed class RunnerException : Exception
{
  public RunnerException(string message)
    : base(message)
  {
  }
}
