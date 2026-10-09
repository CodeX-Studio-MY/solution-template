namespace Acme.Identity.Infrastructure.OpenIddict;

/// <summary>
/// A client registration (catalog database). <see cref="TenantId"/> is null for platform clients (the BFF and
/// platform services), so a token request can find its tenant from its client.
/// </summary>
public sealed class OidcApplication
{
    public Guid Id { get; set; }

    public Guid? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? ClientType { get; set; }

    public string? ApplicationType { get; set; }

    public string? ConsentType { get; set; }

    public string? DisplayName { get; set; }

    public string? DisplayNames { get; set; }

    public string? JsonWebKeySet { get; set; }

    public string? Permissions { get; set; }

    public string? PostLogoutRedirectUris { get; set; }

    public string? RedirectUris { get; set; }

    public string? Requirements { get; set; }

    public string? Settings { get; set; }

    public string? Properties { get; set; }

    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>An authorization (tenant database; platform principals' in the catalog). No navigation to the application.</summary>
public sealed class OidcAuthorization
{
    public Guid Id { get; set; }

    public string? ApplicationId { get; set; }

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Type { get; set; }

    public string? Scopes { get; set; }

    public string? Properties { get; set; }

    public DateTime? CreationDate { get; set; }

    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}

/// <summary>A token entry, including hashed refresh tokens (tenant database; platform principals' in the catalog).</summary>
public sealed class OidcToken
{
    public Guid Id { get; set; }

    public string? ApplicationId { get; set; }

    public string? AuthorizationId { get; set; }

    public string? Subject { get; set; }

    public string? Status { get; set; }

    public string? Type { get; set; }

    public string? Payload { get; set; }

    public string? ReferenceId { get; set; }

    public string? Properties { get; set; }

    public DateTime? CreationDate { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public DateTime? RedemptionDate { get; set; }

    public string? ConcurrencyToken { get; set; } = Guid.NewGuid().ToString();
}
