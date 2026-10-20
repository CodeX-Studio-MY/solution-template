namespace Farm360.Core.Security;

/// <summary>
/// Endpoint metadata for sign-in, token, enrollment and password-reset endpoints: they are rate limited per client address
/// (they run before a tenant or principal is known), shared across instances.
/// </summary>
public sealed class AuthRateLimited
{
    public static AuthRateLimited Instance { get; } = new();
}

/// <summary>
/// Endpoint metadata for pages on the central sign-in host (<c>login.{BaseDomain}</c>), where there is no tenant subdomain:
/// the tenant is named by the <c>tenant</c> query or form parameter, which the tenant's Gateway sets.
/// </summary>
public sealed class TenantFromParameter
{
    public const string Name = "tenant";

    public static TenantFromParameter Instance { get; } = new();
}
