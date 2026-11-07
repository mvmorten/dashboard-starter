using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Email is off (no settings). Nothing should reach this, since forgot-password checks
/// <see cref="EmailStatus.Enabled"/> first, but if something does, it's logged, not sent.
/// </summary>
public sealed class OffEmailSender : IEmailSender
{
  private readonly ILogger<OffEmailSender> _logger;

  public OffEmailSender(ILogger<OffEmailSender> logger)
  {
    _logger = logger;
  }

  public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    _logger.LogWarning("Email is off, so an email to user {UserId} wasn't sent.", message.UserId);
    return Task.CompletedTask;
  }
}
