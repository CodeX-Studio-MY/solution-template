using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Farm360.Core.Configuration;
using Farm360.Core.Platform;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Farm360.Core.Hosting.DataProtection;

/// <summary>
/// The ASP.NET Core Data Protection key ring, shared by every instance in Redis and wrapped (AES-GCM) by a key from the
/// secret store, so a copy of Redis alone can't read it. It protects BFF sessions, antiforgery tokens and stored secrets.
/// </summary>
public static class PlatformDataProtection
{
    public static IServiceCollection AddPlatformDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<KeyRingOptions>(configuration);
        services.AddDataProtection();
        services.AddOptions<KeyManagementOptions>().Configure<IConnectionMultiplexer, IOptions<KeyRingOptions>>((management, redis, options) =>
        {
            management.XmlRepository = new Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository(
                () => redis.GetDatabase(),
                $"{PlatformInfo.Product}:data-protection-keys");
            management.XmlEncryptor = new AesGcmXmlEncryptor(Convert.FromBase64String(options.Value.KeyEncryptionKey));
        });
        services.AddOptions<Microsoft.AspNetCore.DataProtection.DataProtectionOptions>().Configure<IOptions<KeyRingOptions>>((options, ours) =>
            options.ApplicationDiscriminator = ours.Value.ApplicationName);
        return services;
    }
}

/// <summary>Encrypts key-ring elements with AES-256-GCM under the key-encryption key.</summary>
public sealed class AesGcmXmlEncryptor(byte[] key) : IXmlEncryptor
{
    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[plaintext.Length];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, cipher, tag);
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, nonce.Length);
        cipher.CopyTo(payload, nonce.Length + tag.Length);
        return new EncryptedXmlInfo(new XElement("encryptedKey", new XElement("value", Convert.ToBase64String(payload))), typeof(AesGcmXmlDecryptor));
    }
}

/// <summary>
/// Decrypts elements written by <see cref="AesGcmXmlEncryptor"/>. The current key is tried first, then the previous ones, so a
/// key-encryption key can be rotated without invalidating the ring: AES-GCM authenticates, so a wrong key fails cleanly.
/// </summary>
public sealed class AesGcmXmlDecryptor(IServiceProvider services) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var options = services.GetRequiredService<IOptions<KeyRingOptions>>().Value;
        var payload = Convert.FromBase64String(encryptedElement.Element("value")!.Value);
        return Decrypt(payload, [options.KeyEncryptionKey, .. options.PreviousKeyEncryptionKeys]);
    }

    internal static XElement Decrypt(byte[] payload, IEnumerable<string> keys)
    {
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        foreach (var candidate in keys)
        {
            var plaintext = new byte[cipher.Length];
            try
            {
                using var aes = new AesGcm(Convert.FromBase64String(candidate), 16);
                aes.Decrypt(nonce, cipher, tag, plaintext);
                return XElement.Parse(Encoding.UTF8.GetString(plaintext));
            }
            catch (AuthenticationTagMismatchException)
            {
                // Written under another key: try the next one.
            }
        }
        throw new CryptographicException("No configured key-encryption key can read this key-ring entry. Is the previous key missing from DataProtection:PreviousKeyEncryptionKeys?");
    }
}
