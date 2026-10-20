using System.Text.Json;
using Farm360.Identity.Application;
using Farm360.Identity.Infrastructure.OpenIddict;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Farm360.Identity.Infrastructure;

internal sealed class ClientRegistry(IdentityCatalogDbContext db, IOpenIddictApplicationManager manager) : IClientRegistry
{
    public async Task RegisterAsync(string clientId, string displayName, Guid? tenantId, ClientCredential credential, IEnumerable<string> grantTypes, CancellationToken cancellationToken = default)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            DisplayName = displayName,
            ClientType = ClientTypes.Confidential,
            ClientSecret = credential.Secret,
        };
        descriptor.Permissions.Add(Permissions.Endpoints.Token);
        foreach (var grant in grantTypes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.GrantType + grant);
            if (grant == GrantTypes.AuthorizationCode)
            {
                descriptor.Permissions.Add(Permissions.Endpoints.Authorization);
                descriptor.Permissions.Add(Permissions.Endpoints.EndSession);
                descriptor.Permissions.Add(Permissions.ResponseTypes.Code);
                descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            }
        }
        if (credential.PublicKeyJwk is { } jwk)
            descriptor.JsonWebKeySet = new JsonWebKeySet { Keys = { new JsonWebKey(jwk) } };

        if (await manager.FindByClientIdAsync(clientId, cancellationToken) is OidcApplication existing)
            await manager.DeleteAsync(existing, cancellationToken);
        var created = (OidcApplication)await manager.CreateAsync(descriptor, cancellationToken);
        created.TenantId = tenantId;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RegisterIfChangedAsync(string clientId, string displayName, string secret, IEnumerable<string> grantTypes, CancellationToken cancellationToken = default)
    {
        var existing = await db.OidcApplications.SingleOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);
        var grants = grantTypes.ToList();
        if (existing is not null
            && existing.TenantId is null
            && grants.All(g => OidcJson.Strings(existing.Permissions).Contains(Permissions.Prefixes.GrantType + g))
            && await manager.ValidateClientSecretAsync(existing, secret, cancellationToken))
            return;

        var redirects = existing?.RedirectUris;   // survive a re-seed: tenants' callback URIs are data
        await RegisterAsync(clientId, displayName, null, new ClientCredential(Secret: secret), grants, cancellationToken);
        if (redirects is not null)
        {
            var app = await Find(clientId, cancellationToken);
            app.RedirectUris = redirects;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task AddKeyAsync(string clientId, string publicKeyJwk, CancellationToken cancellationToken = default)
    {
        var app = await Find(clientId, cancellationToken);
        var set = OidcJson.KeySet(app.JsonWebKeySet) ?? new JsonWebKeySet();
        var key = new JsonWebKey(publicKeyJwk);
        if (set.Keys.Any(k => k.Kid == key.Kid))
            return;
        set.Keys.Add(key);
        app.JsonWebKeySet = OidcJson.KeySet(set);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveKeyAsync(string clientId, string keyId, CancellationToken cancellationToken = default)
    {
        var app = await Find(clientId, cancellationToken);
        var set = OidcJson.KeySet(app.JsonWebKeySet) ?? new JsonWebKeySet();
        var kept = set.Keys.Where(k => k.Kid != keyId).ToList();
        if (kept.Count == 0)
            throw new InvalidOperationException("A client must keep at least one key.");
        set.Keys.Clear();
        foreach (var key in kept)
            set.Keys.Add(key);
        app.JsonWebKeySet = OidcJson.KeySet(set);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var app = await db.OidcApplications.SingleOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);
        if (app is null)
            return;
        db.OidcApplications.Remove(app);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetBffRedirectUrisAsync(string clientId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default)
    {
        var app = await Find(clientId, cancellationToken);
        // Redirect URIs are exact, never wildcards.
        if (add.Concat(remove).Any(u => u.Contains('*', StringComparison.Ordinal) || !Uri.TryCreate(u, UriKind.Absolute, out _)))
            throw new ArgumentException("Redirect URIs must be exact absolute URIs.");
        var redirects = OidcJson.Strings(app.RedirectUris).Where(u => !remove.Contains(u)).Union(add).Order(StringComparer.Ordinal).ToList();
        app.RedirectUris = JsonSerializer.Serialize(redirects);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<OidcApplication> Find(string clientId, CancellationToken cancellationToken) =>
        await db.OidcApplications.SingleOrDefaultAsync(a => a.ClientId == clientId, cancellationToken)
        ?? throw new InvalidOperationException($"Client '{clientId}' is not registered.");
}
