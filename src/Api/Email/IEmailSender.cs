using System.Threading;
using System.Threading.Tasks;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Sends one email. The rest of the app only knows this interface, so the service behind it
/// (Resend when deployed, a folder of text files in Development) can be swapped without
/// touching anything else. See <see cref="EmailSetup"/> for which one runs.
/// </summary>
public interface IEmailSender
{
  Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>One email, with a plain-text and an HTML version of the same content.</summary>
/// <param name="UserId">Who it's for, for log messages: logs carry ids, never addresses.</param>
public sealed record EmailMessage(int UserId, string To, string Subject, string Text, string Html);
