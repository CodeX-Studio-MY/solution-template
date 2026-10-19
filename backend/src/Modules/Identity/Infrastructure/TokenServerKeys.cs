using System.Security.Cryptography;
using Saaskit.Core.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Saaskit.Identity.Infrastructure;

/// <summary>
/// Loads the token server's keys from configuration (the secret store). The private key exists only in the Identity
/// module. Every signing key has a <c>kid</c>, so keys can rotate while old public keys stay published.
/// </summary>
internal static class TokenServerKeys
{
    public static IReadOnlyList<SecurityKey> SigningKeys(TokenServerOptions options) =>
        options.SigningKeys.Select(pem =>
        {
            var key = ECDsa.Create();
            key.ImportFromPem(pem.Replace("\\n", "\n", StringComparison.Ordinal));   // a PEM kept on one line in an env file uses \n
            if (key.KeySize != 256)
                throw new InvalidOperationException("TokenServer:SigningKeys must be EC P-256 keys (ES256).");
            return (SecurityKey)new ECDsaSecurityKey(key) { KeyId = KeyId(key) };
        }).ToList();

    public static IReadOnlyList<SecurityKey> EncryptionKeys(TokenServerOptions options) =>
        options.EncryptionKeys.Select(base64 =>
        {
            var bytes = Convert.FromBase64String(base64);
            if (bytes.Length != 32)
                throw new InvalidOperationException("TokenServer:EncryptionKeys must be 256-bit keys, base64 encoded.");
            return (SecurityKey)new SymmetricSecurityKey(bytes) { KeyId = Convert.ToHexString(SHA256.HashData(bytes))[..16] };
        }).ToList();

    /// <summary>A stable key ID: the first 16 hex characters of the SHA-256 of the public key.</summary>
    private static string KeyId(ECDsa key) => Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo()))[..16];
}
