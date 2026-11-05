using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Development only: each email becomes a text file in <c>Email:DevFolder</c> (the repo's
/// gitignored <c>.dev-mail/</c> folder), so you can open a reset email and click its link
/// without any email service. CI reads the same files to test the whole reset flow.
/// </summary>
/// <remarks>
/// Only the file name is logged. The link in the file carries a reset token, and tokens stay
/// out of logs in Development too.
/// </remarks>
public sealed class FolderEmailSender : IEmailSender
{
  private readonly string _folder;
  private readonly ILogger<FolderEmailSender> _logger;

  public FolderEmailSender(EmailStatus status, ILogger<FolderEmailSender> logger)
  {
    _folder = status.Folder ?? throw new InvalidOperationException("FolderEmailSender needs Email:DevFolder.");
    _logger = logger;
  }

  public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(_folder);

    // Sorts by time; the random part keeps two emails in the same millisecond apart.
    string name = string.Create(
      CultureInfo.InvariantCulture,
      $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.txt");

    StringBuilder content = new();
    content.Append("To: ").AppendLine(message.To);
    content.Append("Subject: ").AppendLine(message.Subject);
    content.AppendLine();
    content.AppendLine(message.Text);
    content.AppendLine();
    content.AppendLine("---- HTML version ----");
    content.AppendLine(message.Html);

    await File.WriteAllTextAsync(Path.Combine(_folder, name), content.ToString(), cancellationToken);
    _logger.LogInformation("Wrote an email to user {UserId} as {File}.", message.UserId, name);
  }
}
