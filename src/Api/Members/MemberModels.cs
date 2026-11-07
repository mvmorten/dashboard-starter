using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardStarter.Api.Members;

/// <summary>GET /api/members: the current tenant's members, and the roles they can be given.</summary>
public sealed record MembersResponse(IReadOnlyList<MemberResponse> Members, IReadOnlyList<RoleResponse> Roles);

/// <param name="Status">"Active" or "Disabled": a disabled member can't work in this tenant, but keeps their other tenants.</param>
/// <param name="LastSignInAt">The account's last sign-in (UTC), to any tenant. Null: never.</param>
/// <param name="RowVersion">Send it back with a change, so two people editing one member can't overwrite each other.</param>
public sealed record MemberResponse(
  int UserId,
  string DisplayName,
  string Email,
  int RoleId,
  string RoleName,
  string Status,
  DateTime? LastSignInAt,
  byte[] RowVersion);

public sealed record RoleResponse(int Id, string Name, string Description);

/// <summary>Body of PUT /api/members/{userId}/role.</summary>
public sealed record ChangeRoleRequest(int RoleId, [Required] byte[] RowVersion);

/// <summary>Body of PUT /api/members/{userId}/status.</summary>
public sealed record ChangeStatusRequest(
  [Required, RegularExpression("^(Active|Disabled)$")] string Status,
  [Required] byte[] RowVersion);

/// <summary>The answer to a change: the membership's new RowVersion, for the next change.</summary>
public sealed record MemberChangedResponse(byte[] RowVersion);
