using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acme.Core.Composition;
using Acme.Core.Configuration;
using Acme.Core.Errors;
using Acme.Core.Platform;
using Microsoft.Extensions.Options;

namespace Acme.Core.ServiceCalls;

/// <summary>
/// Gets access tokens for calls to other services. System work uses this service's own client-credentials token;
/// acting for a user exchanges the user's token (RFC 8693) for one with the other service's audience, so it still sees the user.
/// Tokens are cached until shortly before they expire.
/// </summary>
public sealed class PlatformTokenProvider(
    IHttpClientFactory clients,
    IOptions<TenancyOptions> tenancy,
    IOptions<AuthOptions> auth,
    IOptions<ServiceClientOptions> serviceClient,
    HostedPlatformServices hosted,
    TimeProvider time)
{
    public const string HttpClientName = "platform-token";
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (string Token, DateTimeOffset Expires)> _cache = new(StringComparer.Ordinal);

    private string ClientId => hosted.All[0].ClientId;

    private Uri TokenEndpoint => new(auth.Value.TokenEndpoint ?? tenancy.Value.Issuer + "connect/token");

    /// <summary>This service's own token for <paramref name="audience"/>, for background or system work.</summary>
    public Task<string> GetServiceTokenAsync(PlatformService audience, CancellationToken cancellationToken) =>
        GetAsync($"svc|{audience.Audience}", new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["resource"] = audience.Audience,
        }, cancellationToken);

    /// <summary>The user's token exchanged for one that <paramref name="audience"/> accepts. <paramref name="tenantSlug"/> names the user's tenant.</summary>
    public Task<string> ExchangeUserTokenAsync(string userAccessToken, string tenantSlug, PlatformService audience, CancellationToken cancellationToken) =>
        GetAsync($"usr|{Hash(userAccessToken)}|{audience.Audience}", new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["subject_token"] = userAccessToken,
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
            ["resource"] = audience.Audience,
            ["tenant"] = tenantSlug,
        }, cancellationToken);

    private async Task<string> GetAsync(string cacheKey, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(cacheKey, out var cached) && cached.Expires - Margin > now)
            return cached.Token;

        form["client_id"] = ClientId;
        form["client_secret"] = serviceClient.Value.ClientSecret
            ?? throw new InvalidOperationException("ServiceClient:ClientSecret is required for service-to-service calls.");
        using var response = await clients.CreateClient(HttpClientName).PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new ForbiddenException(PlatformErrors.ServiceTokenUnavailable, $"The token endpoint answered {(int)response.StatusCode}.");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var token = body.GetProperty("access_token").GetString()!;
        var expires = now.AddSeconds(body.GetProperty("expires_in").GetInt32());
        _cache[cacheKey] = (token, expires);
        return token;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
}
