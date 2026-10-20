using System.Net;
using System.Security.Cryptography;
using System.Text;
using Farm360.Core.Configuration;
using Farm360.Core.Errors;
using Farm360.Core.Security;
using Farm360.Core.Tenancy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Farm360.Gateway.Http;

/// <summary>Resolves the tenant from the host (<c>{tenant}.{BaseDomain}</c>). The browser's session cookie is host-only, so a session never crosses tenants.</summary>
internal sealed class GatewayTenantMiddleware(RequestDelegate next, IOptions<TenancyOptions> tenancy)
{
    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContextSetter setter)
    {
        var slug = Farm360.Core.Tenancy.TenantResolutionMiddleware.SubdomainOf(context.Request.Host.Host, tenancy.Value.BaseDomain);
        if (slug is not null)
        {
            var tenant = await directory.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is null || tenant.Status is TenantStatus.Deleted or TenantStatus.Provisioning)
                throw new NotFoundException(PlatformErrors.TenantNotFound);
            if (tenant.Status == TenantStatus.PendingDeletion)
                throw new ForbiddenException(PlatformErrors.TenantUnavailable);
            setter.SetTenant(tenant);
        }
        await next(context);
    }
}

/// <summary>
/// CSRF: every state-changing request must carry <c>X-CSRF: 1</c>. A cross-site page can't add a custom header without a CORS
/// preflight, and the Gateway answers preflights with a refusal, so forged requests are blocked. Applies to /bff and /api.
/// </summary>
internal sealed class CsrfMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var guarded = path.StartsWithSegments("/bff", StringComparison.Ordinal) || path.StartsWithSegments("/api", StringComparison.Ordinal);
        var method = context.Request.Method;
        var safe = HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        if (guarded && !safe && context.Request.Headers[PlatformHeaders.Csrf] != "1")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = 403, Title = "platform.csrf", Detail = $"Send the {PlatformHeaders.Csrf}: 1 header." });
            return;
        }
        if (guarded && HttpMethods.IsOptions(method))
        {
            // CORS is off: preflights are refused, so cross-origin pages can never send the custom header.
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }
        await next(context);
    }
}

/// <summary>
/// HSTS, a Content-Security-Policy with a per-request style nonce, nosniff and a referrer policy. The CSP allows scripts only from
/// the app and the module CDN, and connections only to the app, the CDN and the WebSocket origin.
/// </summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<FrontendOptions> frontend, IOptions<BffOptions> bff, IOptions<TenancyOptions> tenancy)
{
    public const string NonceItem = "csp-nonce";

    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItem] = nonce;
        var origin = $"{context.Request.Host}";
        var cdn = Origin(frontend.Value.ModuleBaseUrl);
        var extra = bff.Value.ExtraSources;
        var scripts = string.Join(' ', new[] { "'self'", cdn, extra }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var connect = string.Join(' ', new[] { "'self'", $"wss://{origin}", cdn, extra }.Where(s => !string.IsNullOrWhiteSpace(s)));
        // Signing in ends with a redirect from the sign-in host back to the tenant's host, and browsers apply form-action to the
        // redirects that follow a form post. So the sign-in host (and only it) may send forms on to the tenants' hosts.
        var forms = context.Request.Host.Host.Equals($"login.{tenancy.Value.BaseDomain}", StringComparison.OrdinalIgnoreCase)
            ? $"'self' {tenancy.Value.TenantOrigin("*")}"
            : "'self'";

        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy"] =
                $"default-src 'none'; script-src {scripts}; style-src 'self' 'nonce-{nonce}'; img-src 'self' data: blob:; font-src 'self'; " +
                $"connect-src {connect}; frame-ancestors 'none'; base-uri 'self'; form-action {forms}; object-src 'none'";
            return Task.CompletedTask;
        });
        await next(context);
    }

    /// <summary>The origin of an absolute URL, or empty when the module base is a relative path served by the Gateway itself.</summary>
    private static string Origin(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.GetLeftPart(UriPartial.Authority) : "";
}

internal static class HostFilters
{
    /// <summary>Whether this request is for a sign-in host or the bare domain rather than a tenant.</summary>
    public static bool IsTenantHost(HttpContext context) => context.RequestServices.GetRequiredService<ITenantContext>().HasTenant;

    public static HttpStatusCode Unauthorized => HttpStatusCode.Unauthorized;

    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}
