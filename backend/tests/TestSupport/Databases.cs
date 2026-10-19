using Saaskit.Core.Data;
using Saaskit.Core.Data.PostgreSql;
using Saaskit.Core.Data.SqlServer;
using Saaskit.Core.Platform;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Saaskit.TestSupport;

/// <summary>
/// One container per database provider listed in platform.yaml (SQL Server 2025, PostgreSQL 18) for the whole test run. Every test that
/// touches data runs on every listed provider: a feature isn't done until it passes on all of them. PostgreSQL's container only starts
/// when PostgreSQL is listed (scripts/add-postgresql adds it).
/// </summary>
public sealed class Databases : IAsyncLifetime
{
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2025-latest";
    public const string PostgreSqlImage = "postgres:18";
    public const string ValkeyImage = "valkey/valkey:9";
    public const string RabbitMqImage = "rabbitmq:4-management";
    public const string MailpitImage = "axllent/mailpit:v1.27";
    public const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.37.0";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();
    private readonly PostgreSqlContainer? _postgreSql = UsesPostgreSql
        ? new PostgreSqlBuilder(PostgreSqlImage).WithCommand("-c", "max_connections=1000").Build()
        : null;
    private readonly RedisContainer _valkey = new RedisBuilder(ValkeyImage).Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder(RabbitMqImage).Build();
    private readonly IContainer _mailpit = new ContainerBuilder(MailpitImage)
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")))
        .Build();
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(AzuriteImage).WithCommand("--skipApiVersionCheck").Build();

    /// <summary>The providers of platform.yaml: what every data test runs on.</summary>
    public static TheoryData<string> Providers => [.. PlatformInfo.DatabaseProviders];

    /// <summary>The provider the hosted services (Identity, Tenancy, Gateway, ...) run on in tests: the first one listed.</summary>
    public static string DefaultProvider => PlatformInfo.DatabaseProviders[0];

    private static bool UsesPostgreSql => PlatformInfo.DatabaseProviders.Contains(PostgreSqlDatabaseProvider.ProviderName);

    /// <summary>A connection string that is never opened, for tests that only build the model.</summary>
    public static string ModelOnlyConnectionString(string provider) =>
        provider == SqlServerDatabaseProvider.ProviderName ? "Server=model-only;Database=x" : "Host=model-only;Database=x";

    public async ValueTask InitializeAsync()
    {
        // The light containers start together; SQL Server (heavy, and emulated on Apple Silicon) starts after them, with one retry,
        // so a busy Docker VM doesn't make the whole run flaky.
        await Task.WhenAll(_postgreSql?.StartAsync() ?? Task.CompletedTask, _valkey.StartAsync(), _rabbitMq.StartAsync(), _azurite.StartAsync(), _mailpit.StartAsync());
        try
        {
            await _sqlServer.StartAsync();
        }
        catch (Exception)
        {
            await _sqlServer.StopAsync();
            await _sqlServer.StartAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        if (_postgreSql is not null)
            await _postgreSql.DisposeAsync();
        await _valkey.DisposeAsync();
        await _rabbitMq.DisposeAsync();
        await _azurite.DisposeAsync();
        await _mailpit.DisposeAsync();
    }

    /// <summary>Mailpit's SMTP host and port: the mail the Notification module sends lands here.</summary>
    public (string Host, int Port) Smtp => (_mailpit.Hostname, _mailpit.GetMappedPublicPort(1025));

    /// <summary>Mailpit's HTTP API, to read what arrived.</summary>
    public Uri MailpitApi => new($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}");

    /// <summary>RabbitMQ, the message broker (AMQP URI).</summary>
    public string RabbitMqConnectionString => _rabbitMq.GetConnectionString();

    /// <summary>Azurite, the Azure Blob emulator.</summary>
    public string AzuriteConnectionString => _azurite.GetConnectionString();

    /// <summary>Valkey, the Redis-protocol store.</summary>
    public string RedisConnectionString => _valkey.GetConnectionString();

    /// <summary>The server's base connection string (a pool, in placement terms).</summary>
    public string ServerConnectionString(string provider) =>
        provider switch
        {
            SqlServerDatabaseProvider.ProviderName => _sqlServer.GetConnectionString(),
            PostgreSqlDatabaseProvider.ProviderName when _postgreSql is not null => _postgreSql.GetConnectionString(),
            _ => throw new InvalidOperationException($"'{provider}' is not listed in platform.yaml (database.providers); scripts/add-postgresql adds PostgreSQL."),
        };

    public static IDatabaseProvider Provider(string provider) =>
        provider == SqlServerDatabaseProvider.ProviderName ? new SqlServerDatabaseProvider() : new PostgreSqlDatabaseProvider();

    /// <summary>A connection string for a new, uniquely named database on the provider's server.</summary>
    public string NewDatabase(string provider, string prefix = "t") =>
        Provider(provider).BuildConnectionString(ServerConnectionString(provider), UniqueName(prefix), 100);

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];
}
