using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;

namespace Acme.Integration.Tests;

/// <summary>Secrets every platform host needs. Generated per test run; never stored.</summary>
internal static class TestSecrets
{
    public static readonly string KeyEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static void Apply(WebApplicationBuilder builder) =>
        builder.Configuration["DataProtection:KeyEncryptionKey"] = KeyEncryptionKey;
}
