using Acme.Core.Tenancy;
using Acme.Gateway.Proxy;
using Acme.Gateway.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Acme.Gateway.Http;

/// <summary>Context item keys shared between the gate and the proxy transforms.</summary>
internal static class SessionItems
{
    public const string AccessToken = "bff.access-token";
}

/// <summary>
/// Runs before the proxy forwards an /api request: requires a session for this tenant, then gets the access token for the
/// target service's audience and hands it to the transform. The browser never sees the token.
/// </summary>
internal static class SessionGate
{
    public static async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        var audience = context.GetReverseProxyFeature().Route.Config.Metadata?.GetValueOrDefault(ProxyConfiguration.AudienceMetadata);
        if (audience is null)
        {
            await next();   // the central sign-in host: no session, no token
            return;
        }

        var tenant = context.RequestServices.GetRequiredService<ITenantContext>();
        var session = await context.AuthenticateAsync(GatewayAuthentication.CookieScheme);
        if (!tenant.HasTenant || !session.Succeeded
            || session.Principal?.FindFirst(GatewayAuthentication.SessionIdClaim)?.Value is not { } sessionId
            || session.Principal.FindFirst(GatewayAuthentication.TenantClaim)?.Value != tenant.Tenant.Slug)
        {
            await Problem(context, StatusCodes.Status401Unauthorized, "platform.unauthenticated");
            return;
        }

        var token = await context.RequestServices.GetRequiredService<SessionTokenService>()
            .GetAccessTokenAsync(sessionId, tenant.Tenant.Slug, audience, context.RequestAborted);
        if (token is null)
        {
            await context.SignOutAsync(GatewayAuthentication.CookieScheme);
            await Problem(context, StatusCodes.Status401Unauthorized, "platform.session-ended");
            return;
        }

        context.Items[SessionItems.AccessToken] = token;
        await next();
    }

    private static async Task Problem(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = code, Extensions = { ["code"] = code } });
    }
}
