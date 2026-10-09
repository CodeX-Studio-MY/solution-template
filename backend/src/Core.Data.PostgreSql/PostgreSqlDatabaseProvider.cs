using System.Globalization;
using System.Text;
using Acme.Core.Errors;
using Acme.Core.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Acme.Core.Data.PostgreSql;

public sealed class PostgreSqlDatabaseProvider : IDatabaseProvider
{
    public const string ProviderName = "postgresql";
    private const string RowVersion = "RowVersion";

    public string Name => ProviderName;

    public string MigrationsAssemblySuffix => "PostgreSql";

    public string NotDeletedFilter => $"\"{SnakeCase(nameof(ISoftDeletable.IsDeleted))}\" = false";

    public string NotNullFilter(string propertyName) => $"\"{SnakeCase(propertyName)}\" IS NOT NULL";

    public string RegexCheck(string propertyName, string pattern) =>
        $"\"{SnakeCase(propertyName)}\" ~ '{pattern.Replace("'", "''", StringComparison.Ordinal)}'";

    public string BuildConnectionString(string baseConnectionString, string databaseName, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = maxPoolSize,
        }.ConnectionString;

    public void Configure(DbContextOptionsBuilder options, string connectionString, string schema, string migrationsAssembly) =>
        options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history", schema);
                npgsql.MigrationsAssembly(migrationsAssembly);
            })
            // Naming is applied in exactly one place: snake_case tables and columns on PostgreSQL.
            .UseSnakeCaseNamingConvention();

    public void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // Guid keys: Npgsql generates UUIDv7 client-side by default, which sorts correctly on PostgreSQL.
        // DateTime (UTC) maps to timestamptz; Npgsql requires DateTimeKind.Utc, which the UTC rule guarantees.
    }

    /// <summary>The <c>xmin</c> system column is PostgreSQL's row version; no column is created for it.</summary>
    public void ConfigureConcurrencyToken(EntityTypeBuilder entity) =>
        entity.Property<uint>(RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();

    public string? GetConcurrencyToken(EntityEntry entry) =>
        entry.Property(RowVersion).CurrentValue is uint value and > 0 ? value.ToString("x8", CultureInfo.InvariantCulture) : null;

    public void SetOriginalConcurrencyToken(EntityEntry entry, string token)
    {
        if (!uint.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new PreconditionFailedException("The ETag is not valid.");
        entry.Property(RowVersion).OriginalValue = value;
    }

    public void AddServices(IServiceCollection services, string catalogConnectionString)
    {
        services.AddSingleton<ISequenceService, PostgreSqlSequenceService>();
        services.AddSingleton<IDistributedLock, PostgreSqlDistributedLock>();
        services.AddHealthChecks().AddNpgSql(catalogConnectionString, name: "catalog-db", tags: [HealthTags.Ready]);
    }

    internal static string SnakeCase(string name)
    {
        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>One atomic upsert: <c>INSERT … ON CONFLICT DO UPDATE … RETURNING</c>.</summary>
internal sealed class PostgreSqlSequenceService : ISequenceService
{
    public async Task<long> NextAsync(ModuleDbContext db, string sequenceName, CancellationToken cancellationToken = default)
    {
        var (schema, table, name, value) = DbCommandRunner.SequenceCounterNames(db);
        var sql = $"""
            INSERT INTO "{schema}"."{table}" ("{name}", "{value}") VALUES (@name, 1)
            ON CONFLICT ("{name}") DO UPDATE SET "{value}" = "{table}"."{value}" + 1
            RETURNING "{value}";
            """;
        return (long)(await DbCommandRunner.ExecuteScalarAsync(db, sql, new Dictionary<string, object> { ["@name"] = sequenceName }, cancellationToken))!;
    }
}

/// <summary><c>pg_advisory_xact_lock</c>, released when the transaction ends.</summary>
internal sealed class PostgreSqlDistributedLock : IDistributedLock
{
    public async Task AcquireAsync(PlatformDbContext db, string key, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A distributed lock needs an open transaction.");
        await DbCommandRunner.ExecuteScalarAsync(
            db,
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));",
            new Dictionary<string, object> { ["@key"] = key },
            cancellationToken);
    }
}
