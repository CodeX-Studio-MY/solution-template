namespace Saaskit.Tenancy.Application;

/// <summary>One plan as the operator console shows it. <see cref="TenantCount"/> counts tenants on the plan that are not deleted.</summary>
public sealed record PlanRow(
    short PlanId,
    string PlanKey,
    string DisplayName,
    string? Description,
    decimal? MonthlyPrice,
    string Currency,
    byte SortOrder,
    bool IsActive,
    int TenantCount,
    IReadOnlyList<string> Modules,
    string ETag);

/// <summary>What creates a plan. The key never changes afterwards. The currency defaults to the platform's, the sort order to last.</summary>
public sealed record PlanCreate(string PlanKey, string DisplayName, string? Description, decimal? MonthlyPrice, string? Currency = null, byte? SortOrder = null);

public sealed record PlanUpdate(string DisplayName, string? Description, decimal? MonthlyPrice, string Currency, byte SortOrder);

public sealed record ModuleRow(string ModuleKey, string DisplayName);

/// <summary>
/// The plans the platform sells, their prices and their modules. platform.yaml seeds the first plans; from then on the
/// operator console owns this data. Changing a plan's modules changes every tenant on it and tells the services.
/// </summary>
public interface IPlanCatalog
{
    Task<IReadOnlyList<PlanRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<PlanRow> CreateAsync(PlanCreate input, string actor, CancellationToken cancellationToken = default);

    Task<PlanRow> UpdateAsync(short planId, PlanUpdate input, string? ifMatch, string actor, CancellationToken cancellationToken = default);

    /// <summary>Retires or restores a plan. New tenants can't choose a retired plan; tenants already on it stay.</summary>
    Task<PlanRow> SetActiveAsync(short planId, bool active, string? ifMatch, string actor, CancellationToken cancellationToken = default);

    /// <summary>Adds the module to the plan and to every tenant on it.</summary>
    Task<PlanRow> AddModuleAsync(short planId, string moduleKey, string actor, CancellationToken cancellationToken = default);

    /// <summary>Removes the module from the plan and from every tenant on it, add-ons included, as a plan change does.</summary>
    Task<PlanRow> RemoveModuleAsync(short planId, string moduleKey, string actor, CancellationToken cancellationToken = default);

    /// <summary>The modules the platform has. They need code, so the console only reads them.</summary>
    Task<IReadOnlyList<ModuleRow>> ListModulesAsync(CancellationToken cancellationToken = default);
}
