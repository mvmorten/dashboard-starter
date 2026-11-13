using System.Collections.Generic;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// What every demo sandbox is made of: two tenants, the visitor's role in each, and the sample
/// members they see on the Members page. <see cref="SandboxStore"/> creates one from this.
/// </summary>
/// <remarks>
/// <para>
/// The visitor is an Admin in "Demo" and Read-only in "Other Demo", so switching tenants shows
/// the same person with different permissions: controls in one tenant, none in the other.
/// </para>
/// <para>
/// The sample members are real users of this sandbox only, so the visitor can change their
/// roles and disable them. Users.Email is unique across the whole database, so each sandbox's
/// sample members get their own addresses, on the reserved .invalid domain (RFC 2606), which
/// can't belong to anyone. They have no password, so nobody can sign in as them.
/// </para>
/// <para>
/// Role names appear here because this is a definition, like the migration that creates the
/// roles. Code that decides what someone may do checks permissions only.
/// </para>
/// </remarks>
public static class SandboxDefinition
{
  /// <summary>The reserved domain every sandbox address is on. No real account can have one.</summary>
  public const string EmailDomain = "demo.invalid";

  public const string VisitorName = "Demo Guest";

  public static IReadOnlyList<string> Tenants { get; } = ["Demo", "Other Demo"];

  public static IReadOnlyList<SandboxMembership> Visitor { get; } =
  [
    new("Demo", "Admin", "Active"),
    new("Other Demo", "Read-only", "Active"),
  ];

  // Sam is an Admin in both, so "Other Demo" has someone who can manage members: the visitor
  // is Read-only there, and every tenant keeps an admin.
  public static IReadOnlyList<SandboxSampleMember> SampleMembers { get; } =
  [
    new("sam.rivera", "Sam Rivera", [new("Demo", "Admin", "Active"), new("Other Demo", "Admin", "Active")]),
    new("priya.shah", "Priya Shah", [new("Demo", "Member", "Active"), new("Other Demo", "Member", "Active")]),
    new("lee.chen", "Lee Chen", [new("Demo", "Read-only", "Active")]),
    new("jordan.blake", "Jordan Blake", [new("Demo", "Member", "Disabled")]),
  ];
}

public sealed record SandboxMembership(string TenantName, string RoleName, string Status);

/// <param name="EmailName">The part of the address before the sandbox's own suffix: sam.rivera-1a2b3c4d5e6f@demo.invalid.</param>
public sealed record SandboxSampleMember(string EmailName, string DisplayName, IReadOnlyList<SandboxMembership> Memberships);
