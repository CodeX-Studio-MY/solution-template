using System.Security.Claims;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Saaskit.Identity.Application;
using Saaskit.Identity.Infrastructure;
using Saaskit.Identity.Infrastructure.OpenIddict;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Saaskit.Identity.Api;

/// <summary>The OAuth 2.0 / OpenID Connect endpoints at the host root: authorize, token and logout.</summary>
internal static class ConnectEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapMethods("/connect/token", ["POST"], (Delegate)TokenAsync).AllowAnonymous().WithMetadata(AuthRateLimited.Instance);
        app.MapMethods("/connect/authorize", ["GET", "POST"], (Delegate)AuthorizeAsync).AllowAnonymous().WithMetadata(TenantFromParameter.Instance);
        app.MapMethods("/connect/logout", ["GET", "POST"], (Delegate)LogoutAsync).AllowAnonymous().WithMetadata(TenantFromParameter.Instance);
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext http, ITenantContext tenant, IdentityDbContext db, AccessPrincipalFactory factory)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request can't be retrieved.");
        var login = await http.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var signedInTenant = login.Principal?.FindFirstValue(IdentityModuleSetup.TenantCookieClaim);
        if (!login.Succeeded || !string.Equals(signedInTenant, tenant.Tenant.Slug, StringComparison.Ordinal))
        {
            var returnUrl = http.Request.PathBase + http.Request.Path + http.Request.QueryString;
            return Results.Redirect($"/account/login?tenant={Uri.EscapeDataString(tenant.Tenant.Slug)}&returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var principalId = Guid.Parse(login.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var principal = await db.Principals.SingleOrDefaultAsync(p => p.Id == principalId && p.IsActive, http.RequestAborted);
        if (principal is null)
            return Forbid(OpenIddictConstants.Errors.AccessDenied, "The account is not active.");

        var claims = await factory.CreateAsync(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, request.GetScopes(), http.RequestAborted);
        return SignIn(claims, request, TokenAudiences.Resolve(request.GetResources(), PlatformServices.Identity.Audience)!);
    }

    private static async Task<IResult> TokenAsync(
        HttpContext http,
        ITenantContext tenant,
        IServiceProvider services,
        TimeProvider time)
    {
        var request = http.GetOpenIddictServerRequest() ?? throw new InvalidOperationException("The OpenID Connect request can't be retrieved.");
        var audiences = TokenAudiences.Resolve(request.GetResources(), request.IsClientCredentialsGrantType() ? null : PlatformServices.Identity.Audience);
        if (audiences is null)
            return Forbid(OpenIddictConstants.Errors.InvalidTarget, "The resource must be the audience of a platform service.");

        if (request.IsClientCredentialsGrantType())
            return await ClientCredentialsAsync(http, services, request, audiences, time);

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            return await RefreshAsync(http, services, tenant, request, audiences);

        if (request.IsTokenExchangeGrantType())
            return await TokenExchange.HandleAsync(http, services, tenant, request, audiences);

        return Forbid(OpenIddictConstants.Errors.UnsupportedGrantType, "The grant type is not supported.");
    }

    private static async Task<IResult> ClientCredentialsAsync(
        HttpContext http,
        IServiceProvider services,
        OpenIddictRequest request,
        IReadOnlyList<string> audiences,
        TimeProvider time)
    {
        var application = await services.GetRequiredService<OidcApplicationStore>().FindByClientIdAsync(request.ClientId ?? "", http.RequestAborted);
        if (application is null)
            return Forbid(OpenIddictConstants.Errors.InvalidClient, "The client is not known.");

        ClaimsPrincipal claims;
        if (application.TenantId is null)
        {
            // A platform service: no tenant, may name one with X-Tenant (permission tenant:any).
            claims = PlatformServicePrincipal(application.ClientId!, request.GetScopes());
        }
        else
        {
            var db = services.GetRequiredService<IdentityDbContext>();
            var principal = await db.Principals.SingleOrDefaultAsync(p => p.ClientId == application.ClientId && p.IsActive && p.Type != PrincipalType.User, http.RequestAborted);
            if (principal is null)
                return Forbid(OpenIddictConstants.Errors.InvalidClient, "The client is disabled.");
            claims = await services.GetRequiredService<AccessPrincipalFactory>().CreateAsync(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, request.GetScopes(), http.RequestAborted);
            principal.Seen(time.GetUtcNow().UtcDateTime);
            await db.SaveChangesAsync(http.RequestAborted);
        }

        var ttl = application.TenantId is null || claims.GetPrincipalType() == PrincipalType.Service
            ? TimeSpan.FromMinutes(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Saaskit.Core.Configuration.TokenServerOptions>>().Value.ServiceAccessTokenMinutes)
            : (TimeSpan?)null;
        return SignIn(claims, request, audiences, ttl);
    }

    /// <summary>A platform service's own token: <c>principal_type=service</c>, no tenant, <c>tenant:any</c>, and admin in the modules it owns.</summary>
    private static ClaimsPrincipal PlatformServicePrincipal(string clientId, IEnumerable<string> scopes)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, OpenIddictConstants.Claims.Name, "platform:no-role-claim");
        identity.AddClaim(new Claim(OpenIddictConstants.Claims.Subject, clientId).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.PrincipalType, PrincipalType.Service.ToClaimValue()).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.Permissions, PlatformPermissions.TenantAny).WithDestination());
        identity.AddClaim(new Claim(PlatformClaims.AccessVersion, "0", ClaimValueTypes.Integer64).WithDestination());
        var owned = PlatformServices.All.FirstOrDefault(s => s.ClientId == clientId)?.OwnedModules ?? [];
        foreach (var module in owned)
            identity.AddClaim(new Claim(PlatformClaims.Roles, RoleExtensions.ToModuleRole(module, Role.Admin)).WithDestination());
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(scopes);
        return principal;
    }

    /// <summary>Authorization code and refresh: the principal's access is re-resolved, so changes apply on refresh.</summary>
    private static async Task<IResult> RefreshAsync(HttpContext http, IServiceProvider services, ITenantContext tenant, OpenIddictRequest request, IReadOnlyList<string> audiences)
    {
        var result = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = result.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
        if (!Guid.TryParse(subject, out var principalId) || !tenant.HasTenant)
            return Forbid(OpenIddictConstants.Errors.InvalidGrant, "The token is no longer valid.");

        var db = services.GetRequiredService<IdentityDbContext>();
        var principal = await db.Principals.SingleOrDefaultAsync(p => p.Id == principalId && p.IsActive, http.RequestAborted);
        if (principal is null)
            return Forbid(OpenIddictConstants.Errors.InvalidGrant, "The account is no longer active.");

        var claims = await services.GetRequiredService<AccessPrincipalFactory>()
            .CreateAsync(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, result.Principal!.GetScopes(), http.RequestAborted);
        return SignIn(claims, request, audiences);
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(IdentityConstants.ApplicationScheme);
        return Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    internal static IResult SignIn(ClaimsPrincipal principal, OpenIddictRequest request, IReadOnlyList<string> audiences, TimeSpan? accessTokenLifetime = null)
    {
        principal.SetResources(audiences);
        principal.SetClaim("client_id", request.ClientId);
        if (accessTokenLifetime is { } ttl)
            principal.SetAccessTokenLifetime(ttl);
        // Platform claims are access-token claims only (their destinations are set when created); refresh tokens keep the identity.
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    internal static IResult Forbid(string error, string description) =>
        Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
}
