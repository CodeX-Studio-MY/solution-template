using System.ComponentModel.DataAnnotations;

namespace Farm360.Core.Configuration;

[OptionsSection("Onboarding", "How the Tenancy module provisions, migrates and deletes tenants.")]
public sealed record OnboardingOptions
{
    [Required]
    [Setting("The tenant server pool new tenants are placed on (a key of Database:Servers).", Example = "default")]
    public string DefaultServerPool { get; init; } = "default";

    [Setting("Command that applies one tenant's migrations. Tenancy never applies migrations itself; the pipeline's Migrator does. {tenant} is replaced by the tenant ID.", Example = "dotnet")]
    public string MigratorCommand { get; init; } = "dotnet";

    [Setting("Arguments for the migrator command. {tenant} is replaced by the tenant ID.", Example = "Farm360.Migrator.dll --tenant {tenant}")]
    public string MigratorArguments { get; init; } = "";

    [Range(0, 3650)]
    [Setting("Days a tenant stays in PendingDeletion (after its data export) before its database and files are deleted.")]
    public int DeletionGraceDays { get; init; } = 30;
}

[OptionsSection("Frontend", "Where the frontend modules are served from. Per-tenant versions are data in the catalog.")]
public sealed record FrontendOptions
{
    [Required]
    [Setting("Base URL of the module CDN (or '/mfe' when the Gateway serves them). Module entries are {base}/{module-key}/{version}/mf-manifest.json.", Example = "/mfe")]
    public string ModuleBaseUrl { get; init; } = "/mfe";

    [Required]
    [Setting("Module version served to tenants that have no version set in the catalog.")]
    public string DefaultModuleVersion { get; init; } = "0.0.0-dev";
}

[OptionsSection("Operators", "The platform operator realm (our own support staff).")]
public sealed record OperatorOptions
{
    [Range(5, 240)]
    [Setting("How long an operator's console session lasts, in minutes.")]
    public int SessionMinutes { get; init; } = 30;
}
