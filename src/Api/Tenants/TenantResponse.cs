namespace DashboardStarter.Api.Tenants;

/// <summary>A tenant the signed-in user belongs to, as listed in GET /api/auth/me.</summary>
public sealed record TenantResponse(int Id, string Name);
