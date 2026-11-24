using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DashboardStarter.Api.Customers;

/// <summary>GET /api/customers: one page of the current tenant's customers, and how many match in all.</summary>
/// <param name="Page">1-based. A page past the end is empty, with the right <paramref name="Total"/>.</param>
public sealed record CustomerListResponse(IReadOnlyList<CustomerListItem> Items, int Total, int Page, int PageSize);

/// <summary>A row of the list: what the table shows, no more.</summary>
/// <param name="CreatedAt">UTC, like <see cref="CustomerResponse.CreatedAt"/>.</param>
/// <param name="UpdatedAt">UTC: the last change.</param>
/// <param name="DeletedAt">UTC. Null except in the Deleted list (status=Deleted).</param>
public sealed record CustomerListItem(
  int Id, string Name, string? Email, string? Phone, string Status, DateTime CreatedAt, DateTime UpdatedAt, DateTime? DeletedAt);

/// <summary>
/// GET /api/customers' query, after the controller has checked it: every value is one the
/// store accepts.
/// </summary>
/// <param name="Search">Trimmed, or null for none. Matches the start of the name or email.</param>
/// <param name="Status">"Active", "Inactive", or null for both. Null when <paramref name="Deleted"/>.</param>
/// <param name="Deleted">The deleted customers instead of the live ones (status=Deleted).</param>
/// <param name="Sort">A key of <see cref="CustomerStore.SortColumns"/>.</param>
/// <param name="Page">1-based.</param>
public sealed record CustomerListQuery(string? Search, string? Status, bool Deleted, string Sort, bool Descending, int Page);

/// <summary>
/// One customer, as GET /api/customers/{id} returns it, and in the 409 body as "current". A
/// deleted one (GET /api/customers/{id}/deleted) is the same shape with the two Deleted values set.
/// </summary>
/// <param name="UpdatedByName">Who made the last change, for "Sam Rivera changed this". Null if nobody did (a data migration).</param>
/// <param name="DeletedAt">UTC. Null for a live customer.</param>
/// <param name="DeletedByName">Who deleted it. Null for a live customer, or if nobody did (a data migration).</param>
/// <param name="RowVersion">Send it back with a change, a delete or a restore, so two people can't overwrite each other.</param>
public sealed record CustomerResponse(
  int Id,
  string Name,
  string? Email,
  string? Phone,
  string Status,
  string? Notes,
  DateTime CreatedAt,
  DateTime UpdatedAt,
  string? UpdatedByName,
  DateTime? DeletedAt,
  string? DeletedByName,
  byte[] RowVersion);

/// <summary>
/// The fields a person edits: the body of POST /api/customers, and of PUT with a RowVersion.
/// Checked in <see cref="CustomerValidation"/>, not with attributes, so the rules (trimming,
/// empty means none, the column lengths) are in one readable place.
/// </summary>
public record CustomerFields(string? Name, string? Email, string? Phone, string? Status, string? Notes);

/// <summary>Body of PUT /api/customers/{id}: the fields, and the RowVersion they were loaded with.</summary>
public sealed record UpdateCustomerRequest(
  string? Name,
  string? Email,
  string? Phone,
  string? Status,
  string? Notes,
  [Required] byte[] RowVersion)
  : CustomerFields(Name, Email, Phone, Status, Notes);

/// <summary>Body of DELETE /api/customers/{id}: the RowVersion, so a delete can't remove a change you haven't seen.</summary>
public sealed record DeleteCustomerRequest([Required] byte[] RowVersion);

/// <summary>Body of POST /api/customers/{id}/restore: the RowVersion the deleted customer was loaded with.</summary>
public sealed record RestoreCustomerRequest([Required] byte[] RowVersion);
