namespace Farm360.Identity.Application;

/// <summary>How a machine or service client authenticates at the token endpoint.</summary>
/// <param name="PublicKeyJwk">The client's public key as a JWK (<c>private_key_jwt</c>, RFC 7523); the private key never leaves the client.</param>
/// <param name="Secret">Alternatively, a client secret (stored hashed). Services prefer keys.</param>
public sealed record ClientCredential(string? PublicKeyJwk = null, string? Secret = null);

/// <summary>Registers, rotates and removes OAuth client registrations (kept in the catalog, tagged with their tenant).</summary>
public interface IClientRegistry
{
    /// <summary>Registers a client for a tenant (<paramref name="tenantId"/>) or a platform client (null).</summary>
    Task RegisterAsync(string clientId, string displayName, Guid? tenantId, ClientCredential credential, IEnumerable<string> grantTypes, CancellationToken cancellationToken = default);

    /// <summary>Registers a platform client (no tenant) unless one with the same secret and grants already exists. Keeps its redirect URIs.</summary>
    Task RegisterIfChangedAsync(string clientId, string displayName, string secret, IEnumerable<string> grantTypes, CancellationToken cancellationToken = default);

    /// <summary>Adds a public key to the client's key set. Two keys are valid at once during rotation.</summary>
    Task AddKeyAsync(string clientId, string publicKeyJwk, CancellationToken cancellationToken = default);

    /// <summary>Removes the key with <paramref name="keyId"/> after rotation.</summary>
    Task RemoveKeyAsync(string clientId, string keyId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>Registers the callback URIs of a tenant on the BFF client; removed again when the tenant is offboarded.</summary>
    Task SetBffRedirectUrisAsync(string clientId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default);
}
