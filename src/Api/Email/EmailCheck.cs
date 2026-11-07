using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Runs once at startup, like the Data Protection check: settings that can't work stop the
/// app with a message saying which setting to fix, instead of failing quietly the first time
/// someone asks for a password reset.
/// </summary>
public sealed class EmailCheck
{
  private readonly EmailStatus _status;
  private readonly ILogger<EmailCheck> _logger;

  public EmailCheck(EmailStatus status, ILogger<EmailCheck> logger)
  {
    _status = status;
    _logger = logger;
  }

  /// <summary>Returns true if the app can start.</summary>
  public bool Run()
  {
    if (_status.Problem is not null)
    {
      _logger.LogCritical("Email settings are invalid: {Problem}", _status.Problem);
      return false;
    }

    switch (_status.Mode)
    {
      case EmailMode.Resend:
        _logger.LogInformation("Email: sending through Resend, with links to {LinkBase}.", _status.LinkBase);
        break;
      case EmailMode.Folder:
        _logger.LogInformation("Email: writing messages as files to {Folder}, with links to {LinkBase}.", _status.Folder, _status.LinkBase);
        break;
      default:
        _logger.LogInformation("Email: off, so \"Forgot password?\" is hidden. Set Email__From and Email__ResendApiKey to turn it on.");
        break;
    }

    return true;
  }
}
