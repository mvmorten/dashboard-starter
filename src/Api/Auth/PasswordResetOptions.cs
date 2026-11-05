namespace DashboardStarter.Api.Auth;

/// <summary>Password reset settings, from the <c>PasswordReset</c> configuration section.</summary>
public sealed class PasswordResetOptions
{
  public const string SectionName = "PasswordReset";

  public const string RateLimitPolicyName = "password-reset";

  /// <summary>How long a reset link works. Short: the link is as good as the password until used.</summary>
  public int TokenLifetimeSeconds { get; set; } = 1800;

  /// <summary>
  /// Reset emails one address can get per hour. Stops "forgot password" being used to flood
  /// someone's inbox, while leaving room for a real person who asks twice.
  /// </summary>
  public int MaxEmailsPerHour { get; set; } = 3;

  /// <summary>Requests to forgot, reset, and change password allowed per client IP address per window.</summary>
  public int PermitLimit { get; set; } = 5;

  public int WindowSeconds { get; set; } = 60;
}
