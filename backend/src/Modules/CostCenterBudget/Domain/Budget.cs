using Acme.Core.Data;

namespace Acme.CostCenterBudget.Domain;

/// <summary>
/// The budget of one cost center: how much finance allows, and how much approved purchase requests have used so far.
/// </summary>
public sealed class Budget : BaseEntity, IAggregateRoot
{
    public const int MaxCodeLength = 60;
    public const int MaxNameLength = 200;

    private Budget()
    {
    }

    public Budget(string code, string name, decimal amount, string currency)
    {
        Code = code;
        Name = name.Trim();
        Amount = amount;
        Currency = currency;
    }

    /// <summary>The cost center code, as it appears in a purchase request's <c>cost-center</c> field.</summary>
    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public string Currency { get; private set; } = "";

    /// <summary>What finance allows.</summary>
    public decimal Amount { get; private set; }

    /// <summary>What approved requests have used.</summary>
    public decimal Used { get; private set; }

    public decimal Remaining => Amount - Used;

    public void Change(string name, decimal amount)
    {
        Name = name.Trim();
        Amount = amount;
    }

    /// <summary>Uses part of the budget. Going over is allowed (a request is approved by people, not by the budget) and shows as a negative remainder.</summary>
    public void Use(decimal amount) => Used += amount;
}

/// <summary>
/// One approved request that used a budget. The request ID is unique, so a request can use its budget once, whatever the broker
/// does: this is the second line of defence behind the inbox.
/// </summary>
public sealed class BudgetUsage : BaseEntity
{
    private BudgetUsage()
    {
    }

    public BudgetUsage(Guid requestId, string costCenter, decimal amount, string requestNumber)
    {
        RequestId = requestId;
        CostCenter = costCenter;
        Amount = amount;
        RequestNumber = requestNumber;
    }

    public Guid RequestId { get; private set; }

    public string CostCenter { get; private set; } = "";

    public decimal Amount { get; private set; }

    public string RequestNumber { get; private set; } = "";
}
