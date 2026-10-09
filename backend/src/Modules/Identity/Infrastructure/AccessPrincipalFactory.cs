using System.Security.Claims;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace Acme.Identity.Infrastructure;

/// <summary>Builds the claims of an access token for a principal: effective roles only, never raw assignments.</summary>
internal sealed class AccessPrincipalFactory(IdentityDbContext db, ITenantContext tenant)
{
    /// <summary>The principal's effective access in the current tenant.</summary>
    public async Task<EffectiveAccess> ResolveAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var direct = await db.AccessAssignments.Where(a => a.PrincipalId == principalId)
            .Select(a => new ModuleGrant(a.ModuleId, a.Role)).ToListAsync(cancellationToken);
        var directScopes = await db.DataScopeAssignments.Where(s => s.PrincipalId == principalId)
            .Select(s => new ScopeGrant(s.Scope, s.Value)).ToListAsync(cancellationToken);

        var memberships = await (
            from membership in db.GroupMemberships
            join g in db.Groups on membership.GroupId equals g.Id
            where membership.PrincipalId == principalId
            select new { membership.GroupId, MembershipActive = membership.IsActive, GroupActive = g.IsActive })
            .ToListAsync(cancellationToken);
        var groupIds = memberships.Select(m => m.GroupId).ToList();
        var groupGrants = await db.AccessAssignments.Where(a => a.GroupId != null && groupIds.Contains(a.GroupId.Value))
            .Select(a => new { a.GroupId, Grant = new ModuleGrant(a.ModuleId, a.Role) }).ToListAsync(cancellationToken);
        var groupScopes = await db.DataScopeAssignments.Where(s => s.GroupId != null && groupIds.Contains(s.GroupId.Value))
            .Select(s => new { s.GroupId, Scope = new ScopeGrant(s.Scope, s.Value) }).ToListAsync(cancellationToken);

        var groups = memberships.Select(m => new GroupGrants(
            m.GroupActive,
            m.MembershipActive,
            groupGrants.Where(g => g.GroupId == m.GroupId).Select(g => g.Grant).ToList(),
            groupScopes.Where(g => g.GroupId == m.GroupId).Select(g => g.Scope).ToList()));

        return EffectiveAccessResolver.Resolve(direct, directScopes, groups, tenant.Tenant.EnabledModules);
    }

    /// <summary>Creates the identity for a token: <c>sub</c>, <c>principal_type</c>, <c>tenant_id</c>, <c>access_version</c>, <c>roles</c>.</summary>
    public async Task<ClaimsPrincipal> CreateAsync(Principal principal, string scheme, IEnumerable<string> scopes, CancellationToken cancellationToken, string? actor = null)
    {
        var access = await ResolveAsync(principal.Id, cancellationToken);
        var identity = new ClaimsIdentity(scheme, OpenIddictConstants.Claims.Name, "platform:no-role-claim");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, principal.Id.ToString()).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.PrincipalType, principal.Type.ToClaimValue()).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.TenantId, tenant.Tenant.Id.ToString()).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.AccessVersion, principal.AccessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64).WithDestination());
        foreach (var role in access.RoleClaims)
            identity.AddClaim(new Claim(PlatformClaims.Roles, role).WithDestination());
        foreach (var (scope, values) in access.DataScopes)
        {
            foreach (var value in values.Order(StringComparer.Ordinal))
                identity.AddClaim(new Claim(PlatformClaims.DataScope, $"{scope}:{value}").WithDestination());
        }
        if (actor is not null)
            identity.AddClaim(new Claim(PlatformClaims.Actor, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { [PlatformClaims.Subject] = actor }), "JSON").WithDestination());

        var result = new ClaimsPrincipal(identity);
        result.SetScopes(scopes);
        return result;
    }
}

internal static class ClaimDestinations
{
    /// <summary>Platform claims go only into the access token (never into identity tokens or anywhere else).</summary>
    public static Claim WithDestination(this Claim claim) => claim.SetDestinations(OpenIddictConstants.Destinations.AccessToken);
}
