using Acme.Core;
using Acme.Core.Catalog;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Data.SqlServer;
using Acme.Core.Hosting;
using Acme.Core.Hosting.DataProtection;
using Acme.Core.Hosting.Redis;
using Acme.Core.Tenancy;
using Acme.Migrator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Usage: Migrator --catalog | --tenant <id-or-slug> | --all   (flags can be combined; --catalog always runs first)
//        Migrator --create-operator <email> <display name>   (bootstrap: creates a platform operator and prints its credentials once)
var operatorIndex = Array.IndexOf(args, "--create-operator");
var catalog = args.Contains("--catalog");
var all = args.Contains("--all");
var tenantIndex = Array.IndexOf(args, "--tenant");
var tenantArg = tenantIndex >= 0 && tenantIndex + 1 < args.Length ? args[tenantIndex + 1] : null;
if (!catalog && !all && tenantArg is null && operatorIndex < 0)
{
    Console.Error.WriteLine("Usage: Migrator --catalog | --tenant <id-or-slug> | --all | --create-operator <email> <display name>");
    return 2;
}

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults("migrator");
builder.Services.AddPlatformKernel(builder.Configuration);
builder.Services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();
builder.Services.AddSingleton<IDatabaseProvider, PostgreSqlDatabaseProvider>();
builder.Services.AddTenantCatalog();
builder.Services.AddSingleton<MigrationRunner>();
if (operatorIndex >= 0)
{
    // The operator's TOTP secret is protected with the shared Data Protection key ring, so the Tenancy service can read it later.
    builder.Services.AddPlatformRedis(builder.Configuration);
    builder.Services.AddPlatformDataProtection(builder.Configuration);
    Acme.Tenancy.TenancyModule.Register(builder);
}

using var host = builder.Build();
var runner = host.Services.GetRequiredService<MigrationRunner>();
var ct = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
_ = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DatabaseOptions>>().Value;

if (operatorIndex >= 0)
{
    if (args.Length < operatorIndex + 3)
    {
        Console.Error.WriteLine("Usage: Migrator --create-operator <email> <display name>");
        return 2;
    }
    await using var scope = host.Services.CreateAsyncScope();
    var created = await scope.ServiceProvider.GetRequiredService<Acme.Tenancy.Application.IOperatorAccounts>()
        .CreateAsync(args[operatorIndex + 1], args[operatorIndex + 2], ct);
    // Shown once: neither value is stored in clear.
    Console.WriteLine($"Operator {created.Email} created.");
    Console.WriteLine($"Password:  {created.Password}");
    Console.WriteLine($"TOTP key:  {created.TotpSecret}");
    Console.WriteLine($"TOTP link: {created.TotpUri}");
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
