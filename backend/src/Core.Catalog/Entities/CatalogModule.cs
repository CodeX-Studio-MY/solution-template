namespace Farm360.Core.Catalog.Entities;

/// <summary>A module: GUID primary key for references, module key for tokens and code. Seeded from platform.yaml.</summary>
public sealed class CatalogModule
{
    public const int MaxKeyLength = 30;

    public Guid ModuleId { get; init; }

    public string ModuleKey { get; init; } = "";

    public string DisplayName { get; init; } = "";
}

/// <summary>A module included in a plan. Seeded from platform.yaml.</summary>
public sealed class PlanModule
{
    public string Plan { get; init; } = "";

    public Guid ModuleId { get; init; }
}

/// <summary>A module a tenant is entitled to, with the frontend module version it uses.</summary>
public sealed class TenantModule
{
    public Guid TenantId { get; init; }

    public Guid ModuleId { get; init; }

    /// <summary>The frontend module version served to this tenant (canary and rollback are data changes).</summary>
    public string? FrontendVersion { get; set; }
}

public enum MigrationOutcome
{
    Succeeded = 1,
    Failed = 2,
}

/// <summary>The last migration result per tenant and DbContext, so <c>Migrator --all</c> can report and resume.</summary>
public sealed class TenantMigrationState
{
    public Guid TenantId { get; init; }

    /// <summary>The module DbContext, by service key.</summary>
    public string Target { get; init; } = "";

    public MigrationOutcome Outcome { get; set; }

    public string? LastMigration { get; set; }

    public string? Error { get; set; }

    public DateTime UpdatedOn { get; set; }
}

/// <summary>Rate limits for a plan. Plans without a row use the RateLimit configuration defaults.</summary>
public sealed class PlanLimit
{
    public string Plan { get; init; } = "";

    public int PerTenantPerMinute { get; set; }

    public int PerPrincipalPerMinute { get; set; }
}
