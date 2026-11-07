using System;
using System.Net;
using DashboardStarter.Api.Email;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// The "reset your password" email: short, plain text plus a minimal HTML version of the same
/// words. No images and no tracking. It says what to do, how long the link works, and that
/// it's safe to ignore: anyone can type an address into "Forgot password?".
/// </summary>
public static class PasswordResetEmail
{
  public static EmailMessage Create(ResetRecipient recipient, Uri linkBase, string token, int lifetimeSeconds)
  {
    // The token goes in the fragment (#), not the query string (?): browsers never send the
    // fragment to a server, so it can't end up in access logs or a proxy along the way.
    string link = new Uri(linkBase, "/reset-password").AbsoluteUri + "#token=" + token;
    int minutes = Math.Max(1, lifetimeSeconds / 60);

    const string subject = "Reset your Dashboard Starter password";

    string text = $"""
      Someone asked to reset the password for the Dashboard Starter account with this email address.

      To choose a new password, open this link within {minutes} minutes:

      {link}

      The link works once. If you didn't ask for this, you can ignore this email: your password stays as it is.
      """;

    string htmlLink = WebUtility.HtmlEncode(link);
    string html = $"""
      <!DOCTYPE html>
      <html lang="en">
      <body style="font-family: sans-serif; line-height: 1.5;">
      <p>Someone asked to reset the password for the Dashboard Starter account with this email address.</p>
      <p>To choose a new password, open this link within {minutes} minutes:</p>
      <p><a href="{htmlLink}">Reset your password</a></p>
      <p>The link works once. If you didn't ask for this, you can ignore this email: your password stays as it is.</p>
      </body>
      </html>
      """;

    return new EmailMessage(recipient.UserId, recipient.Email, subject, text, html);
  }
}
