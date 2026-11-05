using Saaskit.Core.Data;

namespace Saaskit.Core.Catalog.Entities;

/// <summary>
/// A subscription plan. platform.yaml seeds the first plans once; afterwards the operator console owns them.
/// A plan is retired (<see cref="IsActive"/> off), never deleted: tenants keep it, new tenants can't choose it.
/// </summary>
public sealed class Plan : AuditedEntity, IAggregateRoot
{
    public const int MaxDescriptionLength = 500;
    public const int MaxDisplayNameLength = 100;

    private Plan()
    {
    }

    public Plan(string planKey, string displayName, string? description, decimal? monthlyPrice, string currency, byte sortOrder)
    {
        PlanKey = planKey;
        IsActive = true;
        Change(displayName, description, monthlyPrice, currency, sortOrder);
    }

    public short PlanId { get; private set; }

    /// <summary>Never changes: it is what the API, onboarding and tokens call the plan.</summary>
    public string PlanKey { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public string? Description { get; private set; }

    /// <summary>Empty means no price is set. Display only until billing is built.</summary>
    public decimal? MonthlyPrice { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>Lowest plan first.</summary>
    public byte SortOrder { get; private set; }

    public bool IsActive { get; private set; }

    public void Change(string displayName, string? description, decimal? monthlyPrice, string currency, byte sortOrder)
    {
        DisplayName = displayName;
        Description = description;
        MonthlyPrice = monthlyPrice;
        Currency = currency;
        SortOrder = sortOrder;
    }

    public void SetActive(bool active) => IsActive = active;
}
