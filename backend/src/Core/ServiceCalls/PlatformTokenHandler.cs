using System.Net.Http.Headers;
using Farm360.Core.Platform;
using Farm360.Core.Security;
using Farm360.Core.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Farm360.Core.ServiceCalls;

/// <summary>
/// Attaches the right token to a call to another service (4.10). Acting for a user: the user's token is exchanged
/// for the target's audience. Otherwise (background or system work): this service's own token, plus <c>X-Tenant</c>.
/// </summary>
public sealed class PlatformTokenHandler(
    PlatformService target,
    PlatformTokenProvider tokens,
    IHttpContextAccessor http,
    ITenantContext tenant) : DelegatingHandler
{
    /// <summary>Set on a request to force the service's own token even while handling a user's request.</summary>
    public static readonly HttpRequestOptionsKey<bool> AsSystem = new("platform.as-system");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var incoming = http.HttpContext?.Request.Headers.Authorization.ToString();
        var userToken = incoming is { Length: > 0 } && incoming.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? incoming["Bearer ".Length..] : null;
        var forceSystem = request.Options.TryGetValue(AsSystem, out var forced) && forced;
        var actingForUser = !forceSystem
            && userToken is not null
            && http.HttpContext?.User.GetPrincipalType() == PrincipalType.User
            && tenant.HasTenant;

        string token;
        if (actingForUser)
        {
            token = await tokens.ExchangeUserTokenAsync(userToken!, tenant.Tenant.Slug, target, cancellationToken);
        }
        else
        {
            token = await tokens.GetServiceTokenAsync(target, cancellationToken);
            if (tenant.HasTenant && !request.Headers.Contains(PlatformHeaders.Tenant))
                request.Headers.Add(PlatformHeaders.Tenant, tenant.Tenant.Id.ToString());
        }
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken);
    }
}
