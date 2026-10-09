using System.Diagnostics;
using Acme.Core.Configuration;
using Acme.Core.Errors;
using Acme.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Core.Tenancy;

/// <summary>
/// Resolves the tenant of a request. Runs after authentication.
/// <list type="bullet">
/// <item>Authenticated: the token's <c>tenant_id</c>, nothing else. A subdomain naming another tenant is 403.</item>
/// <item>Platform service principals (no <c>tenant_id</c>, permission <c>tenant:any</c>) name the tenant with <c>X-Tenant</c>.
/// <c>X-Tenant</c> from anyone else is 403.</item>
/// <item>Anonymous: the subdomain of <c>{tenant}.{BaseDomain}</c>.</item>
/// </list>
/// </summary>
public sealed partial class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantDirectory directory, ITenantContextSetter setter)
    {
        var tenant = await ResolveAsync(context, directory);
        if (tenant is null)
        {
            await next(context);
            return;
        }

        setter.SetTenant(tenant);
        Activity.Current?.SetTag(TelemetryTags.TenantId, tenant.Id.ToString());
        using (logger.BeginScope(new Dictionary<string, object> { [TelemetryTags.TenantId] = tenant.Id }))
        {
            await next(context);
        }
    }

    private async Task<TenantInfo?> ResolveAsync(HttpContext context, ITenantDirectory directory)
    {
        var user = context.User;
        var headerTenant = context.Request.Headers[PlatformHeaders.Tenant].ToString();
        var subdomain = SubdomainOf(context.Request.Host.Host, options.Value.BaseDomain);
        var ct = context.RequestAborted;

        if (user.Identity?.IsAuthenticated == true)
        {
            var tokenTenantId = user.GetTenantId();
            if (tokenTenantId is null)
            {
                // No tenant in the token: only a platform service principal may name one.
                if (string.IsNullOrEmpty(headerTenant))
                    return null;
                if (user.GetPrincipalType() != PrincipalType.Service || !user.HasPermission(PlatformPermissions.TenantAny))
                    throw new ForbiddenException(PlatformErrors.TenantHeaderNotAllowed);
                return await FindAsync(directory, headerTenant, ct)
                    ?? throw new NotFoundException(PlatformErrors.TenantNotFound);
            }

            if (!string.IsNullOrEmpty(headerTenant))
                throw new ForbiddenException(PlatformErrors.TenantHeaderNotAllowed);

            var tenant = await directory.FindByIdAsync(tokenTenantId.Value, ct)
                ?? throw new ForbiddenException(PlatformErrors.TenantUnavailable);
            if (subdomain is not null && !string.Equals(subdomain, tenant.Slug, StringComparison.Ordinal))
            {
                LogTenantMismatch(logger, tenant.Id, subdomain);
                throw new ForbiddenException(PlatformErrors.TenantMismatch);
            }
            return tenant;
        }

        if (!string.IsNullOrEmpty(headerTenant))
            throw new ForbiddenException(PlatformErrors.TenantHeaderNotAllowed);
        if (subdomain is null)
            return null;
        return await directory.FindBySlugAsync(subdomain, ct)
            ?? throw new NotFoundException(PlatformErrors.TenantNotFound);
    }

    private static Task<TenantInfo?> FindAsync(ITenantDirectory directory, string value, CancellationToken ct) =>
        Guid.TryParse(value, out var id) ? directory.FindByIdAsync(id, ct) : directory.FindBySlugAsync(value, ct);

    /// <summary>The tenant slug in <c>{tenant}.{baseDomain}</c>, or null for the base domain, <c>login.</c> and other hosts.</summary>
    public static string? SubdomainOf(string host, string baseDomain)
    {
        var suffix = "." + baseDomain;
        if (!host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;
        var label = host[..^suffix.Length].ToLowerInvariant();
        return label.Length == 0 || label.Contains('.', StringComparison.Ordinal) || label == "login" ? null : label;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token tenant {TenantId} does not match subdomain {Subdomain}")]
    private static partial void LogTenantMismatch(ILogger logger, Guid tenantId, string subdomain);
}

/// <summary>Attribute names on logs and spans. <c>tenant.id</c> is never a metric dimension by default (cardinality).</summary>
public static class TelemetryTags
{
    public const string TenantId = "tenant.id";
    public const string PrincipalId = "enduser.id";
    public const string PrincipalType = "enduser.type";
}
