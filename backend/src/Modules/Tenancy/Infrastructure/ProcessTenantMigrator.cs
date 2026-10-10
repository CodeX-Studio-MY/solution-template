using System.Diagnostics;
using Acme.Core.Configuration;
using Acme.Tenancy.Application;
using Microsoft.Extensions.Options;

namespace Acme.Tenancy.Infrastructure;

/// <summary>
/// Runs the configured migrator command for one tenant (<c>Migrator --tenant {id}</c>). Tenancy never applies migrations
/// itself and never references migration code. In a container platform, replace this adapter with one that starts the Migrator job.
/// </summary>
internal sealed class ProcessTenantMigrator(IOptions<OnboardingOptions> options) : ITenantMigrator
{
    public async Task MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        // Arguments is parsed like a command line, so a quoted path with spaces (C:\Users\First Last\...) stays one argument.
        var start = new ProcessStartInfo(settings.MigratorCommand, settings.MigratorArguments.Replace("{tenant}", tenantId.ToString(), StringComparison.Ordinal))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("The migrator could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The migrator exited with {process.ExitCode}: {await errors}{await output}");
    }
}

/// <summary>The default export: nothing to export until modules hold data (milestone 3 replaces this with the per-module export).</summary>
internal sealed class NoExportYet : ITenantExporter
{
    public Task ExportAsync(Acme.Core.Tenancy.TenantInfo tenant, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Billing is out of scope: every tenant is in good standing until a provider is wired in.</summary>
internal sealed class NoBilling : IBillingProvider
{
    public Task<BillingStatus> GetStatusAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new BillingStatus(true, "No billing provider is configured."));
}
