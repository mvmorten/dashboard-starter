using System;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// Demo sandboxes, from the <c>Demo</c> configuration section. Off unless
/// <see cref="SandboxesEnabled"/> is true: a fork shouldn't get an anonymous button that creates
/// accounts unless it asks for one. The live demo's deploy sets Demo__SandboxesEnabled from the
/// repo variable DEMO_ENABLED; Development turns it on in appsettings.Development.json.
/// </summary>
public sealed class DemoOptions
{
  public const string SectionName = "Demo";

  /// <summary>Off: no "Try the demo" button, POST /api/demo/sandbox is a 404, and the cleanup service doesn't run.</summary>
  public bool SandboxesEnabled { get; set; }

  /// <summary>The longest a sandbox lives, however much it's used.</summary>
  public int SandboxHours { get; set; } = 24;

  /// <summary>
  /// A sandbox nobody has used for this long expires early. Each session check (at most once
  /// a minute) moves its expiry forward, up to <see cref="SandboxHours"/> after it was created.
  /// It keeps an abandoned sandbox, or one a script made and never used, from holding a place
  /// under <see cref="MaxActiveSandboxes"/> for a whole day.
  /// </summary>
  public int SandboxIdleMinutes { get; set; } = 120;

  /// <summary>Past this many sandboxes at once, the button answers "The demo is busy".</summary>
  public int MaxActiveSandboxes { get; set; } = 500;

  /// <summary>New sandboxes allowed per client address (IPv4, or IPv6 /64) per window.</summary>
  public int SandboxPermitLimit { get; set; } = 10;

  public int SandboxWindowMinutes { get; set; } = 60;

  /// <summary>How often the cleanup service looks for expired sandboxes. It also runs once at startup.</summary>
  public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
