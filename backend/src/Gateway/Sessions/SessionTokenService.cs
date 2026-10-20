using System.Text.Json;
using Farm360.Core.Configuration;
using Farm360.Core.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Farm360.Gateway.Sessions;

/// <summary>
/// Gets the access token a downstream service accepts, for the signed-in session. Each service has its own audience, so the
/// Gateway asks the token server for one token per service (resource indicators, RFC 8707), caches it, and renews it with the
/// session's refresh token. A refresh token is single use, so concurrent renewals are serialised across Gateway instances.
/// </summary>
internal sealed partial class SessionTokenService(
    IConnectionMultiplexer redis,
    RedisTicketStore tickets,
    IHttpClientFactory clients,
    IOptions<TenancyOptions> tenancy,
    IOptions<AuthOptions> auth,
    IOptions<BffOptions> bff,
    TimeProvider time,
    ILogger<SessionTokenService> logger)
{
    public const string HttpClientName = "bff-token";
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(20);

    private Uri TokenEndpoint => new(auth.Value.TokenEndpoint ?? tenancy.Value.Issuer + "connect/token");

    /// <summary>Returns a valid access token for <paramref name="audience"/>, or null when the session can't be renewed (sign in again).</summary>
    public async Task<string?> GetAccessTokenAsync(string sessionId, string tenantSlug, string audience, CancellationToken cancellationToken)
    {
        var db = redis.GetDatabase();
        if (await CachedAsync(db, sessionId, audience) is { } cached)
            return cached;

        var lockKey = $"{RedisTicketStore.TokensKey(sessionId)}:lock";
        var owner = Guid.NewGuid().ToString("N");
        var deadline = time.GetUtcNow().AddSeconds(8);
        while (!await db.StringSetAsync(lockKey, owner, LockTtl, When.NotExists))
        {
            if (time.GetUtcNow() > deadline)
                return null;
            await Task.Delay(50, cancellationToken);
            // Another request may have just renewed it.
            if (await CachedAsync(db, sessionId, audience) is { } renewed)
                return renewed;
        }

        try
        {
            if (await CachedAsync(db, sessionId, audience) is { } again)
                return again;
            return await RefreshAsync(db, sessionId, tenantSlug, audience, cancellationToken);
        }
        finally
        {
            // Release only our own lock.
            await db.ScriptEvaluateAsync("if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end", [lockKey], [owner]);
        }
    }

    private async Task<string?> RefreshAsync(IDatabase db, string sessionId, string tenantSlug, string audience, CancellationToken cancellationToken)
    {
        var ticket = await tickets.RetrieveAsync(sessionId);
        var refreshToken = ticket?.Properties.GetTokenValue("refresh_token");
        if (ticket is null || string.IsNullOrEmpty(refreshToken))
            return null;

        using var response = await clients.CreateClient(HttpClientName).PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["resource"] = audience,
            ["tenant"] = tenantSlug,
            ["client_id"] = PlatformClients.Bff,
            ["client_secret"] = bff.Value.ClientSecret,
        }), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogRefreshRejected(logger, (int)response.StatusCode, await OAuthErrorAsync(response, cancellationToken), audience);
            // invalid_grant: the refresh token is spent, revoked or expired. The session is over.
            if ((int)response.StatusCode is >= 400 and < 500)
                await tickets.RemoveAsync(sessionId);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var accessToken = body.GetProperty("access_token").GetString()!;
        var lifetime = TimeSpan.FromSeconds(body.GetProperty("expires_in").GetInt32());

        // The refresh token rotates on every use: keep the new one, or the next renewal would count as reuse and revoke the chain.
        if (body.TryGetProperty("refresh_token", out var rotated) && rotated.GetString() is { Length: > 0 } next)
        {
            ticket.Properties.UpdateTokenValue("refresh_token", next);
            await tickets.RenewAsync(sessionId, ticket);
        }

        var ttl = lifetime - Margin;
        if (ttl > TimeSpan.Zero)
        {
            await db.HashSetAsync(RedisTicketStore.TokensKey(sessionId), audience, JsonSerializer.Serialize(new CachedToken(accessToken, time.GetUtcNow().Add(ttl))));
            await db.KeyExpireAsync(RedisTicketStore.TokensKey(sessionId), TimeSpan.FromDays(bff.Value.SessionDays));
        }
        return accessToken;
    }

    /// <summary>The OAuth error name (for example invalid_grant), never the body, which could echo request values.</summary>
    private static async Task<string> OAuthErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            return body.TryGetProperty("error", out var error) ? error.GetString() ?? "unknown" : "unknown";
        }
        catch (JsonException)
        {
            return "unreadable";
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The token server rejected a session renewal: {Status} {Error} for {Audience}")]
    private static partial void LogRefreshRejected(ILogger logger, int status, string error, string audience);

    private async Task<string?> CachedAsync(IDatabase db, string sessionId, string audience)
    {
        var value = await db.HashGetAsync(RedisTicketStore.TokensKey(sessionId), audience);
        if (value.IsNullOrEmpty)
            return null;
        var cached = JsonSerializer.Deserialize<CachedToken>(value.ToString());
        return cached is not null && cached.Expires > time.GetUtcNow() ? cached.Token : null;
    }

    private sealed record CachedToken(string Token, DateTimeOffset Expires);
}
