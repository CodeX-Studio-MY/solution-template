using System.Collections.Concurrent;
using Acme.Core.Catalog;
using Acme.Core.Catalog.Entities;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Migrator;

/// <summary>
/// Applies catalog and tenant migrations. Every migration must be expand-only (backward compatible with the
/// previous release), because tenants are migrated one by one while old and new code both run.
/// </summary>
internal sealed partial class MigrationRunner(
    IServiceScopeFactory scopes,
    IDatabaseProviderAccessor providers,
    ITenantDirectory directory,
    ITenantConnectionResolver connections,
    IOptions<DatabaseOptions> options,
    TimeProvider time,
    ILogger<MigrationRunner> logger)
{
    private readonly IReadOnlyList<MigrationTarget> _catalogTargets = MigrationTargets.Catalog;
    private readonly IReadOnlyList<MigrationTarget> _tenantTargets = MigrationTargets.Tenant;

    public async Task MigrateCatalogAsync(CancellationToken cancellationToken)
    {
        var provider = providers.Current;
        foreach (var target in _catalogTargets)
        {
            await using var db = target.Create(provider, options.Value.CatalogConnectionString);
            var applied = await ApplyAsync(db, cancellationToken);
            LogCatalogMigrated(logger, target.Name, applied);
        }
    }

    /// <summary>Migrates one tenant. Returns false (and records the error) if any target failed.</summary>
    public async Task<bool> MigrateTenantAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        IDatabaseProvider provider;
        string connectionString;
        try
        {
            provider = providers.ForName(tenant.Placement.Provider);
            connectionString = connections.ResolveForMaintenance(tenant);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A tenant that can't be reached is recorded and reported; it never stops the other tenants.
            LogTenantFailed(logger, ex, tenant.Slug, "connection");
            foreach (var target in _tenantTargets)
                await RecordAsync(tenant.Id, target.Name, MigrationOutcome.Failed, null, ex.Message, cancellationToken);
            return false;
        }

        foreach (var target in _tenantTargets)
        {
            try
            {
                await using var db = target.Create(provider, connectionString);
                var applied = await ApplyAsync(db, cancellationToken);
                var last = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
                await RecordAsync(tenant.Id, target.Name, MigrationOutcome.Succeeded, last, null, cancellationToken);
                LogTenantMigrated(logger, tenant.Slug, target.Name, applied);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogTenantFailed(logger, ex, tenant.Slug, target.Name);
                await RecordAsync(tenant.Id, target.Name, MigrationOutcome.Failed, null, ex.Message, cancellationToken);
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Migrates every tenant that isn't deleted, with bounded parallelism. Safe to rerun: tenants already up to date
    /// have nothing pending, so a rerun resumes where a failed run stopped.
    /// </summary>
    public async Task<IReadOnlyList<string>> MigrateAllTenantsAsync(CancellationToken cancellationToken)
    {
        var tenants = await directory.ListAsync(cancellationToken);
        var failed = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            tenants,
            new ParallelOptions { MaxDegreeOfParallelism = options.Value.MigrationParallelism, CancellationToken = cancellationToken },
            async (tenant, ct) =>
            {
                if (!await MigrateTenantAsync(tenant, ct))
                    failed.Add(tenant.Slug);
            });
        LogAllMigrated(logger, tenants.Count, failed.Count);
        return failed.Order(StringComparer.Ordinal).ToList();
    }

    private static async Task<int> ApplyAsync(DbContext db, CancellationToken cancellationToken)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();
        await db.Database.MigrateAsync(cancellationToken);
        return pending;
    }

    private async Task RecordAsync(Guid tenantId, string target, MigrationOutcome outcome, string? last, string? error, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var state = await catalog.MigrationStates.FindAsync([tenantId, target], cancellationToken);
        if (state is null)
        {
            state = new TenantMigrationState { TenantId = tenantId, Target = target };
            catalog.MigrationStates.Add(state);
        }
        state.Outcome = outcome;
        state.LastMigration = last ?? state.LastMigration;
        state.Error = error is null ? null : error[..Math.Min(error.Length, 4000)];
        state.UpdatedOn = time.GetUtcNow().UtcDateTime;
        await catalog.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Catalog target {Target}: {Applied} migration(s) applied")]
    private static partial void LogCatalogMigrated(ILogger logger, string target, int applied);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tenant {Tenant} target {Target}: {Applied} migration(s) applied")]
    private static partial void LogTenantMigrated(ILogger logger, string tenant, string target, int applied);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant {Tenant} target {Target} failed")]
    private static partial void LogTenantFailed(ILogger logger, Exception exception, string tenant, string target);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migrated {Count} tenant(s), {Failed} failed")]
    private static partial void LogAllMigrated(ILogger logger, int count, int failed);
}
