using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// Limits sign-in attempts per client address, with ASP.NET Core's built-in rate limiter.
/// Lockout protects one account from many guesses; this protects every account from one
/// client, including the demo account, which is never locked out.
/// </summary>
/// <remarks>
/// RemoteIpAddress is the real client only because forwarded headers are processed first
/// (ASPNETCORE_FORWARDEDHEADERS_ENABLED on App Service). Without that, every request would
/// appear to come from Azure's front end, and everyone would share one limit.
/// </remarks>
public static class SignInRateLimit
{
  public const string PolicyName = "sign-in";

  public static string PartitionKey(HttpContext context)
  {
    IPAddress? address = context.Connection.RemoteIpAddress;
    if (address is null)
    {
      return "unknown";
    }

    if (address.IsIPv4MappedToIPv6)
    {
      address = address.MapToIPv4();
    }

    if (address.AddressFamily != AddressFamily.InterNetworkV6)
    {
      return address.ToString();
    }

    // One IPv6 customer usually gets a whole /64 and can pick any address in it, so a
    // limit per full address would be easy to step around. Limit the /64 instead.
    byte[] bytes = address.GetAddressBytes();
    for (int index = 8; index < bytes.Length; index++)
    {
      bytes[index] = 0;
    }

    return new IPAddress(bytes) + "/64";
  }
}
