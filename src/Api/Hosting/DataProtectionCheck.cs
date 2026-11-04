using System;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Runs once at startup: can the app load (or create) its Data Protection keys? Without
/// them nobody can sign in, so the app refuses to start and says why, instead of failing
/// the first sign-in with an error nobody sees. See <see cref="DataProtectionSetup"/>.
/// </summary>
public sealed class DataProtectionCheck
{
  private readonly IDataProtectionProvider _provider;
  private readonly IConfiguration _configuration;
  private readonly IHostEnvironment _environment;
  private readonly ILogger<DataProtectionCheck> _logger;

  public DataProtectionCheck(
    IDataProtectionProvider provider,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DataProtectionCheck> logger)
  {
    _provider = provider;
    _configuration = configuration;
    _environment = environment;
    _logger = logger;
  }

  /// <summary>Returns true if the app can start.</summary>
  public bool Run()
  {
    if (!DataProtectionSetup.TryReadAzureSettings(_configuration, out AzureKeyStorage? azure, out string? problem))
    {
      _logger.LogCritical("Data Protection settings are invalid: {Problem}", problem);
      return false;
    }

    // Protecting a value forces the key ring to load, which reads the blob and asks
    // Key Vault to unwrap the keys (or creates the first key on a fresh deployment).
    try
    {
      IDataProtector protector = _provider.CreateProtector("StartupCheck");
      protector.Unprotect(protector.Protect("ok"));
    }
    catch (Exception exception)
    {
      _logger.LogCritical(
        exception,
        "Data Protection keys can't be loaded or created from {Storage}. Sign-in can't work without them.",
        azure?.BlobUri.ToString() ?? "local storage");
      return false;
    }

    if (azure is not null)
    {
      _logger.LogInformation("Data Protection keys are in {BlobUri}, wrapped with Key Vault key {KeyId}.", azure.BlobUri, azure.KeyId);
      return true;
    }

    if (!_environment.IsDevelopment())
    {
      _logger.LogWarning(
        "Data Protection keys are stored on this machine, so a redeploy or a new instance may sign everyone out. "
          + "Set DataProtection__BlobUri and DataProtection__KeyId to keep them in Azure (see docs/deploy-azure.md).");
    }

    return true;
  }
}
