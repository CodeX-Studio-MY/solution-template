using System.Security.Claims;
using Acme.Core.Configuration;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Acme.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Acme.Identity.Api;

/// <summary>
/// RFC 8693 token exchange, using OpenIddict's built-in flow. Two uses:
/// 1. A module acting for a user calls another module: the user's token is exchanged for one with that module's audience,
///    so the other module still sees the user.
/// 2. A user signed in on a shared machine: the user's token plus the machine's token (<c>actor_token</c>) become one delegated token.
///    <c>sub</c> is the user, the <c>act</c> claim identifies the machine; the request is authorized with the user's roles
///    and the audit log records both.
/// </summary>
internal static class TokenExchange
{
    public static async Task<IResult> HandleAsync(HttpContext http, IServiceProvider services, ITenantContext tenant, OpenIddictRequest request, IReadOnlyList<string> audiences)
    {
        // Only the platform's own services exchange tokens (acting for a user in another module).
        if (!PlatformServices.All.Any(s => s.ClientId == request.ClientId))
            return ConnectEndpoints.Forbid(OpenIddictConstants.Errors.UnauthorizedClient, "This client may not exchange tokens.");

        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subjectToken = result.Principal;
        if (subjectToken is null || !tenant.HasTenant)
            return ConnectEndpoints.Forbid(OpenIddictConstants.Errors.InvalidGrant, "The subject token is not valid.");

        // The subject must belong to this tenant, be a user, and still be current (revoked access can't be exchanged).
        if (subjectToken.GetClaim(PlatformClaims.TenantId) != tenant.Tenant.Id.ToString()
            || subjectToken.GetClaim(PlatformClaims.PrincipalType) != PrincipalType.User.ToClaimValue()
            || !Guid.TryParse(subjectToken.GetClaim(OpenIddictConstants.Claims.Subject), out var userId))
            return ConnectEndpoints.Forbid(OpenIddictConstants.Errors.InvalidGrant, "The subject token is not valid for this tenant.");

        var db = services.GetRequiredService<IdentityDbContext>();
        var user = await db.Principals.SingleOrDefaultAsync(p => p.Id == userId && p.IsActive && p.Type == PrincipalType.User, http.RequestAborted);
        if (user is null || subjectToken.GetClaim(PlatformClaims.AccessVersion) != user.AccessVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
            return ConnectEndpoints.Forbid(OpenIddictConstants.Errors.InvalidGrant, "The user's access changed; sign in again.");

        Principal? machine = null;
        if (!string.IsNullOrEmpty(request.ActorToken))
        {
            machine = await ValidateActorAsync(http, services, tenant, db, request.ActorToken);
            if (machine is null)
                return ConnectEndpoints.Forbid(OpenIddictConstants.Errors.InvalidGrant, "The actor token is not a valid machine token for this tenant.");
        }

        var claims = await services.GetRequiredService<AccessPrincipalFactory>().CreateAsync(
            user, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, request.GetScopes(), http.RequestAborted, actor: machine?.Id.ToString());

        var audit = services.GetRequiredService<IAuditWriter>();
        await audit.WriteAsync(
            machine is null ? AuditKinds.TokenIssued : AuditKinds.DelegatedTokenIssued,
            subject: user.Id.ToString(),
            details: new { audiences, exchange = true },
            actorId: user.Id.ToString(),
            actorType: PrincipalType.User.ToClaimValue(),
            onBehalfOf: machine?.Id.ToString(),
            cancellationToken: http.RequestAborted);
        return ConnectEndpoints.SignIn(claims, request, audiences);
    }

    /// <summary>Validates the machine's own access token (ES256, our issuer, identity audience) and loads its principal.</summary>
    private static async Task<Principal?> ValidateActorAsync(HttpContext http, IServiceProvider services, ITenantContext tenant, IdentityDbContext db, string actorToken)
    {
        var tokenServer = services.GetRequiredService<IOptions<TokenServerOptions>>().Value;
        var tenancy = services.GetRequiredService<IOptions<TenancyOptions>>().Value;
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(actorToken, new TokenValidationParameters
        {
            ValidIssuer = tenancy.Issuer,
            ValidAudience = PlatformServices.Identity.Audience,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = ["at+jwt"],
            IssuerSigningKeys = TokenServerKeys.SigningKeys(tokenServer),
            ClockSkew = TimeSpan.FromSeconds(30),
        });
        if (!validation.IsValid)
            return null;
        var claims = new ClaimsPrincipal(validation.ClaimsIdentity);
        if (claims.GetPrincipalType() != PrincipalType.Machine || claims.GetTenantId() != tenant.Tenant.Id
            || !Guid.TryParse(claims.FindFirstValue("sub"), out var machineId))
            return null;
        return await db.Principals.SingleOrDefaultAsync(p => p.Id == machineId && p.IsActive && p.Type == PrincipalType.Machine, http.RequestAborted);
    }
}
