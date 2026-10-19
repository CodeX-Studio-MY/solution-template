using System.Net.Http.Json;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Tenancy.Application;

namespace Saaskit.Tenancy.Infrastructure;

/// <summary>Typed client for the Identity module's platform endpoints. The service-token handler adds the bearer token.</summary>
internal sealed class IdentityClient(HttpClient http) : IIdentityClient
{
    private static readonly string Platform = $"{PlatformServices.Identity.RoutePrefix}/v1/platform";

    public async Task<Uri?> InviteFirstAdminAsync(Guid tenantId, string email, string displayName, IEnumerable<string> modules, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Platform}/invitations", tenantId, new { email, displayName, modules }, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            return null;   // already invited: onboarding resumed
        await EnsureAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<InvitationBody>(cancellationToken);
        return body?.Url;
    }

    public async Task SetBffRedirectUrisAsync(Guid tenantId, IReadOnlyCollection<string> add, IReadOnlyCollection<string> remove, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Put, $"{Platform}/bff-redirect-uris", tenantId, new { add, remove }, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    public async Task RevokeAllTokensAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Platform}/revoke-all-tokens", tenantId, null, cancellationToken);
        await EnsureAsync(response, cancellationToken);
    }

    public async Task<SupportTokenDto> IssueSupportTokenAsync(Guid tenantId, SupportTokenCall request, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"{Platform}/support-tokens", tenantId, request, cancellationToken);
        await EnsureAsync(response, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<SupportTokenDto>(cancellationToken))!;
    }

    /// <summary>Like EnsureSuccessStatusCode, but keeps the problem details Identity answered with.</summary>
    private static async Task EnsureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Identity answered {(int)response.StatusCode} {response.ReasonPhrase}: {(body.Length > 500 ? body[..500] : body)}", null, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, Guid tenantId, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(PlatformHeaders.Tenant, tenantId.ToString());
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await http.SendAsync(request, cancellationToken);
    }

    private sealed record InvitationBody(Uri? Url);
}
