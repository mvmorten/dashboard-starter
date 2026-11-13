using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// Background work in this starter: removes expired demo sandboxes once at startup, then every
/// <see cref="DemoOptions.CleanupInterval"/>. Registered only when sandboxes are on (Program.cs).
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="BackgroundService"/> runs for the life of the app, beside the requests. It uses
/// <see cref="PeriodicTimer"/>, which is built in: no scheduling library. A run that fails is
/// logged and the timer carries on, so one bad night doesn't stop cleanup for good.
/// </para>
/// <para>
/// Every instance of the app runs this. <see cref="SandboxCleanup"/> takes a database lock, so
/// only one of them cleans at a time and the others skip that run.
/// </para>
/// </remarks>
public sealed class SandboxCleanupService : BackgroundService
{
  private readonly SandboxCleanup _cleanup;
  private readonly DemoOptions _options;
  private readonly ILogger<SandboxCleanupService> _logger;

  public SandboxCleanupService(SandboxCleanup cleanup, IOptions<DemoOptions> options, ILogger<SandboxCleanupService> logger)
  {
    _cleanup = cleanup;
    _options = options.Value;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Demo cleanup: every {Interval}, and now.", _options.CleanupInterval);
    using PeriodicTimer timer = new(_options.CleanupInterval);
    try
    {
      do
      {
        await RunOnceAsync(stoppingToken);
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
    {
      // The app is shutting down.
    }
  }

  private async Task RunOnceAsync(CancellationToken stoppingToken)
  {
    try
    {
      await _cleanup.RunAsync(stoppingToken);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
      _logger.LogError(exception, "Demo cleanup failed. It runs again in {Interval}.", _options.CleanupInterval);
    }
  }
}
