using System.Security.Cryptography;
using System.Text;
using Saaskit.Core.Hosting.Auth;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Saaskit.TestSupport;

/// <summary>Mints access tokens the way the token server does (ES256, typ at+jwt), so services can be tested alone.</summary>
public sealed class TestTokens : IDisposable
{
    public const string Issuer = "https://login.localhost/";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public TestTokens() => SigningKey = new ECDsaSecurityKey(_key) { KeyId = "test-key" };

    public ECDsaSecurityKey SigningKey { get; }

    public string Create(
        Guid? tenantId,
        PrincipalType type = PrincipalType.User,
        IEnumerable<string>? roles = null,
        string? audience = null,
        string? subject = null,
        long accessVersion = 0,
        DateTime? expires = null,
        string tokenType = PlatformTokenValidation.AccessTokenType,
        IEnumerable<string>? permissions = null,
        SigningCredentials? credentials = null)
    {
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.Subject] = subject ?? Guid.NewGuid().ToString(),
            [PlatformClaims.PrincipalType] = type.ToString().ToLowerInvariant(),
            [PlatformClaims.AccessVersion] = accessVersion,
        };
        if (tenantId is not null)
            claims[PlatformClaims.TenantId] = tenantId.Value.ToString();
        if (roles is not null)
            claims[PlatformClaims.Roles] = roles.ToArray();
        if (permissions is not null)
            claims[PlatformClaims.Permissions] = permissions.ToArray();

        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience ?? PlatformServices.Notification.Audience,
            Claims = claims,
            IssuedAt = now.AddMinutes(-1),
            NotBefore = now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(15),
            TokenType = tokenType,
            SigningCredentials = credentials ?? new SigningCredentials(SigningKey, SecurityAlgorithms.EcdsaSha256),
        });
    }

    /// <summary>The same token with an edited payload and the original signature.</summary>
    public static string Tamper(string token, Func<string, string> editPayload)
    {
        var parts = token.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]));
        parts[1] = Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(editPayload(payload)));
        return string.Join('.', parts);
    }

    /// <summary>An unsigned token (<c>alg: none</c>).</summary>
    public static string WithoutSignature(string token)
    {
        var parts = token.Split('.');
        var header = Base64UrlEncoder.Encode("""{"alg":"none","typ":"at+jwt"}""");
        return $"{header}.{parts[1]}.";
    }

    public void Dispose() => _key.Dispose();
}
