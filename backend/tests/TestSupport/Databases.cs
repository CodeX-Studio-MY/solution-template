using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Acme.TestSupport;

/// <summary>
/// One SQL Server 2025 and one PostgreSQL 18 container for the whole test run. Every test that touches data runs on both:
/// a feature isn't done until it passes on both providers.
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
    private readonly PostgreSqlContainer _postgreSql = new PostgreSqlBuilder(PostgreSqlImage).WithCommand("-c", "max_connections=1000").Build();
    private readonly RedisContainer _valkey = new RedisBuilder(ValkeyImage).Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder(RabbitMqImage).Build();
    private readonly IContainer _mailpit = new ContainerBuilder(MailpitImage)
        .WithPortBinding(1025, true)
        .WithPortBinding(8025, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")))
        .Build();
    private readonly AzuriteContainer _azurite = new AzuriteBuilder(AzuriteImage).WithCommand("--skipApiVersionCheck").Build();

    public static TheoryData<string> Providers => [SqlServerDatabaseProvider.ProviderName, PostgreSqlDatabaseProvider.ProviderName];

    public async ValueTask InitializeAsync()
    {
        // The light containers start together; SQL Server (heavy, and emulated on Apple Silicon) starts after them, with one retry,
        // so a busy Docker VM doesn't make the whole run flaky.
        await Task.WhenAll(_postgreSql.StartAsync(), _valkey.StartAsync(), _rabbitMq.StartAsync(), _azurite.StartAsync(), _mailpit.StartAsync());
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
        provider == SqlServerDatabaseProvider.ProviderName ? _sqlServer.GetConnectionString() : _postgreSql.GetConnectionString();

    public static IDatabaseProvider Provider(string provider) =>
        provider == SqlServerDatabaseProvider.ProviderName ? new SqlServerDatabaseProvider() : new PostgreSqlDatabaseProvider();

    /// <summary>A connection string for a new, uniquely named database on the provider's server.</summary>
    public string NewDatabase(string provider, string prefix = "t") =>
        Provider(provider).BuildConnectionString(ServerConnectionString(provider), UniqueName(prefix), 100);

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..24];
}
