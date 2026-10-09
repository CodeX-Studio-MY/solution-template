namespace Acme.Core.Security;

/// <summary>Claim and header names shared by every service.</summary>
public static class PlatformClaims
{
    public const string Subject = "sub";
    public const string PrincipalType = "principal_type";
    public const string TenantId = "tenant_id";
    public const string AccessVersion = "access_version";
    public const string Actor = "act";
    public const string Roles = "roles";
    public const string Entitlements = "entitlements";
    public const string DataScope = "data_scope";
    public const string Permissions = "permissions";
}

public static class PlatformHeaders
{
    /// <summary>Names the tenant for platform service principals only.</summary>
    public const string Tenant = "X-Tenant";
    public const string Csrf = "X-CSRF";
    public const string IdempotencyKey = "Idempotency-Key";
}

public static class PlatformPermissions
{
    /// <summary>Lets a platform service principal act for any tenant through <see cref="PlatformHeaders.Tenant"/>.</summary>
    public const string TenantAny = "tenant:any";
}

public enum PrincipalType
{
    User,
    Machine,
    Service,
}

public static class PrincipalTypeExtensions
{
    /// <summary>The lowercase name used in tokens and audit entries, for example <c>machine</c>.</summary>
    public static string ToClaimValue(this PrincipalType type) => type.ToString().ToLowerInvariant();
}
