using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;

namespace Farm360.TestSupport;

/// <summary>Settings and secrets every platform host needs. Secrets are generated per test run; never stored.</summary>
public static class TestSecrets
{
    public static readonly string KeyEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>A folder for the local file provider, removed with the run's temp files.</summary>
    public static readonly string LocalFiles = Path.Combine(Path.GetTempPath(), $"farm360-test-files-{Guid.NewGuid():N}");

    public static void Apply(WebApplicationBuilder builder, Databases databases)
    {
        foreach (var (key, value) in Settings(databases))
            builder.Configuration[key] = value;
    }

    /// <summary>The configuration shared by every host under test.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Settings(Databases databases) =>
    [
        new("DataProtection:KeyEncryptionKey", KeyEncryptionKey),
        new("RabbitMq:ConnectionString", databases.RabbitMqConnectionString),
        new("Storage:Provider", "local"),
        new("Storage:LocalPath", LocalFiles),
    ];
}
