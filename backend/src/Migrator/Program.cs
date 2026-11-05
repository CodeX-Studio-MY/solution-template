using Saaskit.Core;
using Saaskit.Core.Catalog;
using Saaskit.Core.Configuration;
using Saaskit.Core.Data;
using Saaskit.Core.Data.PostgreSql;
using Saaskit.Core.Data.SqlServer;
using Saaskit.Core.Hosting;
using Saaskit.Core.Hosting.DataProtection;
using Saaskit.Core.Hosting.Redis;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Saaskit.Migrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Usage: Migrator --catalog | --tenant <id-or-slug> | --all   (flags can be combined; --catalog always runs first)
//        Migrator --create-operator <email> <display name>   (bootstrap: creates a platform operator and prints its credentials once)
//        Migrator --reset-operator <email>                   (recovery: new password and TOTP key for an operator who lost them)
var operatorIndex = Array.IndexOf(args, "--create-operator");
var resetIndex = Array.IndexOf(args, "--reset-operator");
var catalog = args.Contains("--catalog");
var all = args.Contains("--all");
var tenantIndex = Array.IndexOf(args, "--tenant");
var tenantArg = tenantIndex >= 0 && tenantIndex + 1 < args.Length ? args[tenantIndex + 1] : null;
if (!catalog && !all && tenantArg is null && operatorIndex < 0 && resetIndex < 0)
{
    Console.Error.WriteLine("Usage: Migrator --catalog | --tenant <id-or-slug> | --all | --create-operator <email> <display name> | --reset-operator <email>");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults("migrator");
builder.Services.AddPlatformKernel(builder.Configuration);
builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
builder.Services.AddTenantCatalog();
builder.Services.AddSingleton<MigrationRunner>();
if (operatorIndex >= 0 || resetIndex >= 0)
{
    // The operator's TOTP secret is protected with the shared Data Protection key ring, so the Tenancy service can read it later.
    builder.Services.AddPlatformRedis(builder.Configuration);
    builder.Services.AddPlatformDataProtection(builder.Configuration);
    Saaskit.Tenancy.TenancyModule.Register(builder);
}

using var host = builder.Build();
var runner = host.Services.GetRequiredService<MigrationRunner>();
var ct = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
var databaseOptions = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;
if (!PlatformInfo.DatabaseProviders.Contains(databaseOptions.Provider))
{
    // A provider that platform.yaml doesn't list has no migrations in this build.
    Console.Error.WriteLine($"This build has no migrations for '{databaseOptions.Provider}' (platform.yaml lists: {string.Join(", ", PlatformInfo.DatabaseProviders)}). PostgreSQL is not supported in this release (ADR 0011).");
    return 2;
}

if (operatorIndex >= 0)
{
    if (args.Length < operatorIndex + 3)
    {
        Console.Error.WriteLine("Usage: Migrator --create-operator <email> <display name>");
        return 2;
    }
    await using var scope = host.Services.CreateAsyncScope();
    var created = await scope.ServiceProvider.GetRequiredService<Saaskit.Tenancy.Application.IOperatorAccounts>()
        .CreateAsync(args[operatorIndex + 1], args[operatorIndex + 2], ct);
    PrintOperator(created, "created");
    return 0;
}

if (resetIndex >= 0)
{
    if (args.Length < resetIndex + 2)
    {
        Console.Error.WriteLine("Usage: Migrator --reset-operator <email>");
        return 2;
    }
    await using var scope = host.Services.CreateAsyncScope();
    var reset = await scope.ServiceProvider.GetRequiredService<Saaskit.Tenancy.Application.IOperatorAccounts>()
        .ResetCredentialsAsync(args[resetIndex + 1], ct);
    PrintOperator(reset, "has new sign-in details");
    return 0;
}

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

// Shown once: neither value is stored in clear.
static void PrintOperator(Saaskit.Tenancy.Application.NewOperator op, string what)
{
    Console.WriteLine($"Operator {op.Email} {what}.");
    Console.WriteLine($"Password:  {op.Password}");
    Console.WriteLine($"TOTP key:  {op.TotpSecret}");
    Console.WriteLine($"TOTP link: {op.TotpUri}");
}
