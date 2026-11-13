namespace DashboardStarter.Api.Auth;

/// <summary>Sign-in settings, from the <c>Auth</c> configuration section.</summary>
public sealed class AuthOptions
{
  public const string SectionName = "Auth";

  /// <summary>How long a session lasts without activity. Sliding: each visit past the halfway point renews it.</summary>
  public int SessionHours { get; set; } = 8;

  /// <summary>
  /// How long the API trusts its last check of a session (security stamp, lockout, tenant
  /// membership) before asking the database again. This is the longest a removed user can
  /// keep working.
  /// </summary>
  public int SessionCheckSeconds { get; set; } = 60;

  /// <summary>Wrong passwords in a row before the account is locked.</summary>
  public int MaxFailedSignIns { get; set; } = 5;

  public int LockoutMinutes { get; set; } = 15;

  /// <summary>Sign-in attempts allowed per client IP address per window.</summary>
  public int SignInPermitLimit { get; set; } = 10;

  public int SignInWindowSeconds { get; set; } = 60;
}
