using System;
using System.Collections.Generic;
using DashboardStarter.Db.Audit;

namespace DashboardStarter.Api.Audit;

/// <summary>
/// One page of audit entries, newest first: GET /api/audit, and GET /api/customers/{id}/history.
/// </summary>
/// <param name="Page">1-based. A page past the end is empty, with the right <paramref name="Total"/>.</param>
public sealed record AuditListResponse(IReadOnlyList<AuditEntryResponse> Items, int Total, int Page, int PageSize);

/// <summary>One audit entry: who did what to which entity, and when.</summary>
/// <param name="OccurredAt">UTC.</param>
/// <param name="UserId">Who did it. Null when no person did (the Db runner).</param>
/// <param name="UserName">Their display name as it is now, joined when the log is read. Null with <paramref name="UserId"/>.</param>
/// <param name="EntityType">One of <see cref="AuditLog.EntityTypes"/>.</param>
/// <param name="EntityId">The customer's id, or for a Member the member's user id.</param>
/// <param name="EntityName">The customer's or member's name as it is now. Null if it can't be found.</param>
/// <param name="EntityDeleted">A customer that is deleted now: link to its deleted page, not its form.</param>
/// <param name="Action">Created, Updated, Deleted or Restored.</param>
/// <param name="Changes">The fields that changed, by the API's field name. Null for Deleted and Restored.</param>
public sealed record AuditEntryResponse(
  long Id,
  DateTime OccurredAt,
  int? UserId,
  string? UserName,
  string EntityType,
  int EntityId,
  string? EntityName,
  bool EntityDeleted,
  string Action,
  IReadOnlyDictionary<string, AuditFieldChange>? Changes);

/// <summary>What to read, after the controller has checked it: every value is one the store accepts.</summary>
/// <param name="EntityType">One of <see cref="AuditLog.EntityTypes"/>, or null for all.</param>
/// <param name="EntityId">With <paramref name="EntityType"/>: one entity's history. Null for all.</param>
/// <param name="UserId">Only what this user did, or null for everyone.</param>
/// <param name="Page">1-based.</param>
public sealed record AuditQuery(string? EntityType, int? EntityId, int? UserId, int Page);
