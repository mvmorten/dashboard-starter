using System;
using System.Security.Cryptography;
using DashboardStarter.Db.Passwords;

namespace DashboardStarter.Api.Auth;

/// <summary>
/// A real hash of a random password nobody knows. When someone signs in with an email that
/// has no account (or no password yet), the API still verifies against this, so the answer
/// takes as long as a wrong password would. Otherwise response time would reveal which
/// emails have accounts.
/// </summary>
public sealed class UnknownUserHash
{
  public UnknownUserHash(IPasswordHasher hasher)
  {
    Value = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
  }

  public string Value { get; }
}
