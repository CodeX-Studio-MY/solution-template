using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Acme.Integration.Tests.Databases))]

namespace Acme.Integration.Tests;

/// <summary>
/// One SQL Server 2025 and one PostgreSQL 18 container for the whole test run. Every test that touches data runs on both:
/// a feature isn't done until it passes on both providers.
/// </summary>
public sealed class Databases : IAsyncLifetime
{
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2025-latest";
    public const string PostgreSqlImage = "postgres:18";

    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(SqlServerImage).Build();
    private readonly PostgreSqlContainer _postgreSql = new PostgreSqlBuilder(PostgreSqlImage).Build();

    public static TheoryData<string> Providers => [SqlServerDatabaseProvider.ProviderName, PostgreSqlDatabaseProvider.ProviderName];

    public async ValueTask InitializeAsync() =>
        await Task.WhenAll(_sqlServer.StartAsync(), _postgreSql.StartAsync());

    public async ValueTask DisposeAsync()
    {
        await _sqlServer.DisposeAsync();
        await _postgreSql.DisposeAsync();
    }

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
