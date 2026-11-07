using System;
using System.IO;
using System.Net.Http.Headers;
using DashboardStarter.Api.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DashboardStarter.Api.Email;

/// <summary>
/// Decides, once at startup, whether the app sends email and how, from the <c>Email</c>
/// configuration section and <c>CanonicalHost</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Resend</b>: <c>Email:From</c> and <c>Email:ResendApiKey</c> both set (the deploy
/// sets them when EMAIL_ENABLED is true). Needs <c>CanonicalHost</c> outside Development,
/// because reset links are built from it.</item>
/// <item><b>Folder</b>: Development only, <c>Email:DevFolder</c> set. Each email becomes a text
/// file you can open, so password reset works locally without any email service.</item>
/// <item><b>Off</b>: neither. The app runs as usual; the sign-in page hides "Forgot
/// password?" and nothing is sent. This is where a fork starts.</item>
/// </list>
/// Settings that don't add up (half of Resend's, a folder outside Development) are reported by
/// <see cref="EmailCheck"/>, which stops the app at startup rather than fail at the first reset.
/// </remarks>
public static class EmailSetup
{
  public const string ResendClientName = "resend";

  // Vite's dev server: in Development, links point at the page the developer is looking at.
  private static readonly Uri DevelopmentLinkBase = new("http://localhost:5173");

  public static void AddEmail(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
  {
    EmailStatus status = Read(configuration, environment);
    services.AddSingleton(status);
    services.AddSingleton<EmailCheck>();
    services.AddSingleton<EmailQueue>();
    services.AddHostedService<EmailQueueWorker>();

    switch (status.Mode)
    {
      case EmailMode.Resend:
        services.AddHttpClient(ResendClientName, client =>
        {
          client.BaseAddress = new Uri("https://api.resend.com/");
          client.Timeout = TimeSpan.FromSeconds(10);
          client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", status.ResendApiKey);

          // Resend refuses requests without a User-Agent, with a 403 that looks like a bad key.
          client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DashboardStarter", BuildInfo.Current.Number ?? "dev"));
        });
        services.AddSingleton<IEmailSender, ResendEmailSender>();
        break;
      case EmailMode.Folder:
        services.AddSingleton<IEmailSender, FolderEmailSender>();
        break;
      default:
        services.AddSingleton<IEmailSender, OffEmailSender>();
        break;
    }
  }

  public static EmailStatus Read(IConfiguration configuration, IHostEnvironment environment)
  {
    string? from = Blank(configuration["Email:From"]);
    string? apiKey = Blank(configuration["Email:ResendApiKey"]);
    string? devFolder = Blank(configuration["Email:DevFolder"]);
    string? canonicalHost = Blank(configuration["CanonicalHost"]);
    bool isDevelopment = environment.IsDevelopment();

    Uri? linkBase = null;
    if (canonicalHost is not null)
    {
      // A host name, never anything from the request: a forged Host header must not be able
      // to send someone a reset link to the attacker's own site.
      if (!Uri.TryCreate("https://" + canonicalHost, UriKind.Absolute, out linkBase) || linkBase.Host != canonicalHost.ToLowerInvariant())
      {
        return EmailStatus.Invalid("CanonicalHost must be a host name only, such as dashboard.example.com.");
      }
    }
    else if (isDevelopment)
    {
      linkBase = DevelopmentLinkBase;
    }

    if ((from is null) != (apiKey is null))
    {
      return EmailStatus.Invalid("Set both Email__From and Email__ResendApiKey, or neither.");
    }

    if (from is not null && apiKey is not null)
    {
      if (!from.Contains('@'))
      {
        return EmailStatus.Invalid("Email__From must be an address, such as \"Dashboard Starter <noreply@notify.example.com>\".");
      }

      if (linkBase is null)
      {
        return EmailStatus.Invalid("Email is on, but CanonicalHost isn't set, and reset links are built from it. Set CanonicalHost.");
      }

      return new EmailStatus(EmailMode.Resend, linkBase, from, apiKey, null, null);
    }

    if (devFolder is not null)
    {
      if (!isDevelopment)
      {
        return EmailStatus.Invalid("Email:DevFolder is for Development only: it writes reset links to disk.");
      }

      string folder = Path.GetFullPath(Path.Combine(environment.ContentRootPath, devFolder));
      return new EmailStatus(EmailMode.Folder, linkBase, null, null, folder, null);
    }

    return new EmailStatus(EmailMode.Off, null, null, null, null, null);
  }

  private static string? Blank(string? value)
  {
    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
  }
}

public enum EmailMode
{
  Off,
  Folder,
  Resend,
}

/// <summary>What <see cref="EmailSetup"/> decided at startup.</summary>
/// <param name="LinkBase">Where links in emails point: https://CanonicalHost, or Vite in Development.</param>
/// <param name="Problem">Why the settings are unusable; <see cref="EmailCheck"/> then stops the app.</param>
public sealed record EmailStatus(
  EmailMode Mode,
  Uri? LinkBase,
  string? From,
  string? ResendApiKey,
  string? Folder,
  string? Problem)
{
  /// <summary>Whether password reset emails can be sent. The sign-in page shows "Forgot password?" only then.</summary>
  public bool Enabled => Problem is null && Mode != EmailMode.Off && LinkBase is not null;

  public static EmailStatus Invalid(string problem)
  {
    return new EmailStatus(EmailMode.Off, null, null, null, null, problem);
  }

  // Records print every property by default. Never let the API key reach a log or an exception message.
  public override string ToString()
  {
    return $"EmailStatus {{ Mode = {Mode}, LinkBase = {LinkBase} }}";
  }
}
