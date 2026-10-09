using Acme.Core;
using Acme.Core.Catalog;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Acme.Core.Hosting;
using Acme.Core.Tenancy;
using Acme.Migrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Usage: Migrator --catalog | --tenant <id-or-slug> | --all   (flags can be combined; --catalog always runs first)
var catalog = args.Contains("--catalog");
var all = args.Contains("--all");
var tenantIndex = Array.IndexOf(args, "--tenant");
var tenantArg = tenantIndex >= 0 && tenantIndex + 1 < args.Length ? args[tenantIndex + 1] : null;
if (!catalog && !all && tenantArg is null)
{
    Console.Error.WriteLine("Usage: Migrator --catalog | --tenant <id-or-slug> | --all");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults("migrator");
builder.Services.AddPlatformKernel(builder.Configuration);
builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
builder.Services.AddTenantCatalog();
builder.Services.AddSingleton<MigrationRunner>();

using var host = builder.Build();
var runner = host.Services.GetRequiredService<MigrationRunner>();
var ct = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
_ = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

if (catalog)
    await runner.MigrateCatalogAsync(ct);

if (tenantArg is not null)
{
    var directory = host.Services.GetRequiredService<ITenantDirectory>();
    var tenant = Guid.TryParse(tenantArg, out var id)
        ? await directory.FindByIdAsync(id, ct)
        : await directory.FindBySlugAsync(tenantArg, ct);
    if (tenant is null)
    {
        Console.Error.WriteLine($"Tenant '{tenantArg}' was not found.");
        return 1;
    }
    if (!await runner.MigrateTenantAsync(tenant, ct))
        return 1;
}

if (all)
{
    var failed = await runner.MigrateAllTenantsAsync(ct);
    if (failed.Count > 0)
    {
        Console.Error.WriteLine($"Failed tenants (rerun --all to resume): {string.Join(", ", failed)}");
        return 1;
    }
}

return 0;
