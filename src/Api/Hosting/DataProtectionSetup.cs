using System;
using Azure.Core;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DashboardStarter.Api.Hosting;

/// <summary>
/// Where the Data Protection keys live. Those keys encrypt the auth and antiforgery cookies,
/// so whoever has them can read and forge sessions, and losing them signs everyone out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deployed</b> (both <c>DataProtection:BlobUri</c> and <c>DataProtection:KeyId</c> set, by
/// the deploy pipeline): the key ring is one XML blob in Azure Blob Storage, and every key in
/// it is encrypted ("wrapped") with an RSA key in Azure Key Vault that never leaves the vault.
/// Deploys and restarts reuse the same keys, so nobody is signed out. Reading the blob alone
/// gets an attacker nothing; they'd also need the app's identity to ask Key Vault to unwrap.
/// </para>
/// <para>
/// The app signs in to both with its managed identity (no secrets in settings), using
/// <see cref="ManagedIdentityCredential"/> directly rather than DefaultAzureCredential, which
/// tries a chain of credential sources and is hard to reason about when one fails.
/// </para>
/// <para>
/// This is the one Azure-specific piece in the app. Without the two settings (local
/// development, CI, a fork hosted elsewhere) ASP.NET Core's default applies: keys in a folder
/// in the user profile. Fine on a laptop; on a server it usually means every deploy signs
/// everyone out, so <see cref="DataProtectionCheck"/> logs a warning outside Development.
/// </para>
/// </remarks>
public static class DataProtectionSetup
{
  // A fixed name, so every instance and every deploy of the app shares one key ring,
  // whatever folder it runs from.
  public const string ApplicationName = "DashboardStarter";

  public static void AddDataProtectionKeys(IServiceCollection services, IConfiguration configuration)
  {
    IDataProtectionBuilder dataProtection = services.AddDataProtection().SetApplicationName(ApplicationName);

    // Half-configured settings are reported by DataProtectionCheck at startup, which then
    // stops the app. Here they just mean "don't configure Azure".
    if (!TryReadAzureSettings(configuration, out AzureKeyStorage? azure, out _) || azure is null)
    {
      return;
    }

    TokenCredential credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);
    dataProtection
      .PersistKeysToAzureBlobStorage(azure.BlobUri, credential)
      .ProtectKeysWithAzureKeyVault(azure.KeyId, credential);
  }

  /// <summary>
  /// Reads the two settings. Returns false with a problem if they're half set or not URLs;
  /// true with null storage if neither is set.
  /// </summary>
  public static bool TryReadAzureSettings(IConfiguration configuration, out AzureKeyStorage? storage, out string? problem)
  {
    storage = null;
    problem = null;
    string? blobUri = configuration["DataProtection:BlobUri"];
    string? keyId = configuration["DataProtection:KeyId"];

    if (string.IsNullOrWhiteSpace(blobUri) && string.IsNullOrWhiteSpace(keyId))
    {
      return true;
    }

    if (!Uri.TryCreate(blobUri, UriKind.Absolute, out Uri? blob) || !Uri.TryCreate(keyId, UriKind.Absolute, out Uri? key))
    {
      problem = "Set both DataProtection__BlobUri and DataProtection__KeyId as absolute URLs, or neither.";
      return false;
    }

    storage = new AzureKeyStorage(blob, key);
    return true;
  }
}

public sealed record AzureKeyStorage(Uri BlobUri, Uri KeyId);
