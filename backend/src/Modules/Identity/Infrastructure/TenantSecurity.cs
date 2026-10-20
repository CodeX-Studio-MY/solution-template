using Farm360.Core.Platform;
using Farm360.Identity.Application;
using Farm360.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Farm360.Identity.Infrastructure;

internal sealed class TenantSecurity(IdentityDbContext db, IClientRegistry clients, IAuditWriter audit) : ITenantSecurity
{
    public async Task<long> RevokeAllTokensAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await db.OidcTokens.Where(t => t.Status != OpenIddictConstants.Statuses.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
        await db.OidcAuthorizations.Where(a => a.Status != OpenIddictConstants.Statuses.Revoked)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, OpenIddictConstants.Statuses.Revoked), cancellationToken);
        // Access tokens are short-lived and carry access_version: raise every principal's version so they stop working at once.
        await db.Principals.ExecuteUpdateAsync(s => s.SetProperty(p => p.AccessVersion, p => p.AccessVersion + 1), cancellationToken);
        await audit.WriteAsync(AuditKinds.PrincipalChanged, details: new { revokedAllTokens = true, tokens }, cancellationToken: cancellationToken);
        return tokens;
    }

    public Task SetBffRedirectUrisAsync(IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default) =>
        clients.SetBffRedirectUrisAsync(PlatformClients.Bff, add, remove, cancellationToken);
}
