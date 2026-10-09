using System.Text.Json;
using Acme.Core.Configuration;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Gateway.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Acme.Gateway.Http;

/// <summary>The browser-facing BFF endpoints: login, logout, the signed-in user, and the frontend's runtime configuration.</summary>
internal static class BffEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff");

        bff.MapGet("/login", (string? returnUrl, ITenantContext tenant) =>
            tenant.HasTenant
                ? Results.Challenge(new AuthenticationProperties { RedirectUri = LocalOrRoot(returnUrl) }, [GatewayAuthentication.OidcScheme])
                : Results.NotFound()).AllowAnonymous();

        bff.MapPost("/logout", (HttpContext http) =>
            Results.SignOut(new AuthenticationProperties { RedirectUri = "/" }, [GatewayAuthentication.CookieScheme, GatewayAuthentication.OidcScheme])).AllowAnonymous();

        bff.MapGet("/user", UserAsync).AllowAnonymous();
        bff.MapGet("/config", Config).AllowAnonymous();
    }

    /// <summary>The claims the UI needs, including <c>roles</c>. 401 when there is no session. Roles come from the identity-service token, renewed as needed.</summary>
    private static async Task<IResult> UserAsync(HttpContext http, ITenantContext tenant, SessionTokenService tokens)
    {
        var result = await http.AuthenticateAsync(GatewayAuthentication.CookieScheme);
        if (!result.Succeeded || result.Principal?.FindFirst(GatewayAuthentication.SessionIdClaim)?.Value is not { } sessionId)
            return Results.Unauthorized();
        if (!tenant.HasTenant || result.Principal.FindFirst(GatewayAuthentication.TenantClaim)?.Value != tenant.Tenant.Slug)
            return Results.Unauthorized();

        var token = await tokens.GetAccessTokenAsync(sessionId, tenant.Tenant.Slug, PlatformServices.Identity.Audience, http.RequestAborted);
        if (token is null)
        {
            await http.SignOutAsync(GatewayAuthentication.CookieScheme);
            return Results.Unauthorized();
        }

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token);
        var roles = jwt.Claims.Where(c => c.Type == PlatformClaims.Roles).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray();
        return Results.Json(new
        {
            subject = jwt.Subject,
            tenant = tenant.Tenant.Slug,
            principalType = jwt.GetClaim(PlatformClaims.PrincipalType).Value,
            roles,
        }, options: JsonSerializerOptions.Web);
    }

    /// <summary>
    /// Frontend runtime configuration (build once, deploy everywhere): the same typed options the backend validates,
    /// plus the tenant. No environment values are baked into the frontend build.
    /// </summary>
    private static IResult Config(ITenantContext tenant, IOptions<FrontendOptions> frontend) =>
        Results.Json(new
        {
            tenant = tenant.HasTenant ? new { slug = tenant.Tenant.Slug, name = tenant.Tenant.DisplayName, timeZone = tenant.Tenant.TimeZone, currency = tenant.Tenant.Currency, locale = tenant.Tenant.Locale } : null,
            defaults = new { timeZone = PlatformInfo.DefaultTimeZone, currency = PlatformInfo.DefaultCurrency, locales = PlatformInfo.DefaultLocales },
            modules = new { baseUrl = frontend.Value.ModuleBaseUrl },
            apiPrefix = "/api",
        }, options: JsonSerializerOptions.Web);

    /// <summary>Only local paths are followed after sign-in, never another site.</summary>
    private static string LocalOrRoot(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl[0] == '/' && !returnUrl.StartsWith("//", StringComparison.Ordinal) && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
