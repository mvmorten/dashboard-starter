namespace DashboardStarter.Api.Tenants;

/// <summary>One tenant in the GET /api/tenants list.</summary>
public sealed record TenantResponse(int Id, string Name);
