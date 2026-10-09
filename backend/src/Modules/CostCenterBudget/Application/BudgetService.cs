using Acme.Core.Errors;
using Acme.Core.Platform;
using Acme.Core.Tenancy;
using Acme.CostCenterBudget.Domain;
using Microsoft.Extensions.Logging;

namespace Acme.CostCenterBudget.Application;

public sealed record BudgetDto(string Code, string Name, string Currency, decimal Amount, decimal Used, decimal Remaining, string ETag);

public sealed record SaveBudgetInput(string Name, decimal Amount);

/// <summary>The module's store, as the use cases see it.</summary>
public interface IBudgetRepository
{
    Task<IReadOnlyList<Budget>> ListAsync(CancellationToken cancellationToken = default);

    Task<Budget?> FindAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> UsageExistsAsync(Guid requestId, CancellationToken cancellationToken = default);

    void Add(Budget budget);

    void AddUsage(BudgetUsage usage);

    string ETagOf(Budget budget);

    void ApplyIfMatch(Budget budget, string? ifMatch);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

[ErrorCatalog(Modules.CostCenterBudget)]
public static class CostCenterBudgetErrors
{
    public static readonly ErrorCode NotFound = new("cost-center-budget.not-found", 404, "errors.costCenterBudget.notFound");
}

/// <summary>Finance sets budgets; approved purchase requests use them up.</summary>
public sealed partial class BudgetService(IBudgetRepository budgets, ITenantContext tenant, ILogger<BudgetService> logger)
{
    public async Task<IReadOnlyList<BudgetDto>> ListAsync(CancellationToken cancellationToken = default) =>
        [.. (await budgets.ListAsync(cancellationToken)).Select(ToDto)];

    public async Task<BudgetDto> GetAsync(string code, CancellationToken cancellationToken = default) =>
        ToDto(await budgets.FindAsync(Normalise(code), cancellationToken) ?? throw new NotFoundException(CostCenterBudgetErrors.NotFound));

    /// <summary>Creates the cost center's budget or changes its amount. The amount already used stays.</summary>
    public async Task<BudgetDto> SaveAsync(string code, string? ifMatch, SaveBudgetInput input, CancellationToken cancellationToken = default)
    {
        code = Normalise(code);
        var errors = new ValidationBuilder()
            .Required("code", code, "Code").MaxLength("code", code, Budget.MaxCodeLength, "Code")
            .Required("name", input.Name, "Name").MaxLength("name", input.Name, Budget.MaxNameLength, "Name");
        if (input.Amount < 0)
            errors.Add("amount", "validation.range", "Amount", 0, 1_000_000_000);
        errors.ThrowIfAny();

        var budget = await budgets.FindAsync(code, cancellationToken);
        if (budget is null)
        {
            budget = new Budget(code, input.Name, input.Amount, tenant.Tenant.Currency);
            budgets.Add(budget);
        }
        else
        {
            budgets.ApplyIfMatch(budget, ifMatch);
            budget.Change(input.Name, input.Amount);
        }
        await budgets.SaveAsync(cancellationToken);
        return ToDto(budget);
    }

    /// <summary>
    /// Uses an approved request's total from its cost center's budget, exactly once. A request without a cost center, or for a cost
    /// center that has no budget, uses nothing. Called by the event consumer; safe to call twice for the same request.
    /// </summary>
    public async Task UseAsync(Guid requestId, string requestNumber, string? costCenter, decimal total, string currency, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(costCenter))
            return;
        if (await budgets.UsageExistsAsync(requestId, cancellationToken))
            return;
        var budget = await budgets.FindAsync(Normalise(costCenter), cancellationToken);
        if (budget is null || budget.Currency != currency)
        {
            LogNoBudget(logger, requestNumber, costCenter);
            return;
        }
        budget.Use(total);
        budgets.AddUsage(new BudgetUsage(requestId, budget.Code, total, requestNumber));
        await budgets.SaveAsync(cancellationToken);
    }

    private BudgetDto ToDto(Budget b) => new(b.Code, b.Name, b.Currency, b.Amount, b.Used, b.Remaining, budgets.ETagOf(b));

    private static string Normalise(string code) => code.Trim().ToUpperInvariant();

    [LoggerMessage(Level = LogLevel.Information, Message = "Request {Number} was approved for cost center {CostCenter}, which has no usable budget; nothing was used")]
    private static partial void LogNoBudget(ILogger logger, string number, string costCenter);
}
