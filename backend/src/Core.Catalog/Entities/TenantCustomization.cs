namespace Acme.Core.Catalog.Entities;

/// <summary>A tenant's setting overrides for one module (a JSON object holding only the values that differ from the defaults).</summary>
public sealed class TenantSetting
{
    public const int MaxJsonLength = 250_000;

    public Guid TenantId { get; init; }

    public string ModuleKey { get; init; } = "";

    public string Json { get; set; } = "{}";

    public DateTime UpdatedOn { get; set; }

    public string UpdatedBy { get; set; } = "";
}

/// <summary>A feature flag a tenant has switched on or off explicitly. Flags without a row use their declared default.</summary>
public sealed class TenantFeature
{
    public const int MaxKeyLength = 120;

    public Guid TenantId { get; init; }

    public string FeatureKey { get; init; } = "";

    public bool Enabled { get; set; }

    public DateTime UpdatedOn { get; set; }

    public string UpdatedBy { get; set; } = "";
}
