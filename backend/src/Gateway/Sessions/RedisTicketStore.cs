using Acme.Core.Configuration;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Acme.Gateway.Sessions;

/// <summary>
/// Browser sessions kept server-side in Redis. The cookie holds only an opaque session ID; the ticket (claims and the
/// OAuth tokens) is encrypted with Data Protection and never reaches the browser.
/// </summary>
internal sealed class RedisTicketStore(IConnectionMultiplexer redis, IDataProtectionProvider protection, IOptions<BffOptions> options) : ITicketStore
{
    private readonly IDataProtector _protector = protection.CreateProtector("gateway.session.ticket");

    public static string Key(string sessionId) => $"{PlatformInfo.Product}:bff:session:{sessionId}";

    public static string TokensKey(string sessionId) => $"{PlatformInfo.Product}:bff:tokens:{sessionId}";

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var id = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await WriteAsync(id, ticket);
        return id;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket) => await WriteAsync(key, ticket);

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(key));
        if (value.IsNullOrEmpty)
            return null;
        try
        {
            var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect((byte[])value!));
            // The cookie handler hands back the stored principal, so the session ID is added here for the code that needs the session's tokens.
            if (ticket is not null && ticket.Principal.Identity is System.Security.Claims.ClaimsIdentity identity && !identity.HasClaim(c => c.Type == GatewayAuthentication.SessionIdClaim))
                identity.AddClaim(new System.Security.Claims.Claim(GatewayAuthentication.SessionIdClaim, key));
            return ticket;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;   // key rotated away or tampered: the session is gone
        }
    }

    public async Task RemoveAsync(string key)
    {
        var db = redis.GetDatabase();
        await db.KeyDeleteAsync([Key(key), TokensKey(key)]);
    }

    private async Task WriteAsync(string key, AuthenticationTicket ticket) =>
        await redis.GetDatabase().StringSetAsync(Key(key), _protector.Protect(TicketSerializer.Default.Serialize(ticket)), TimeSpan.FromDays(options.Value.SessionDays));
}
