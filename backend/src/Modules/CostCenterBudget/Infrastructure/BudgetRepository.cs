using Acme.Core.Data;
using Acme.CostCenterBudget.Application;
using Acme.CostCenterBudget.Domain;
using Microsoft.EntityFrameworkCore;

namespace Acme.CostCenterBudget.Infrastructure;

internal sealed class BudgetRepository(CostCenterBudgetDbContext db) : IBudgetRepository
{
    public async Task<IReadOnlyList<Budget>> ListAsync(CancellationToken cancellationToken = default) =>
        // Tracked on purpose: the ETag is read from the tracked row version.
        await db.Budgets.OrderBy(b => b.Code).ToListAsync(cancellationToken);

    public Task<Budget?> FindAsync(string code, CancellationToken cancellationToken = default) =>
        db.Budgets.SingleOrDefaultAsync(b => b.Code == code, cancellationToken);

    public Task<bool> UsageExistsAsync(Guid requestId, CancellationToken cancellationToken = default) =>
        db.Usages.AnyAsync(u => u.RequestId == requestId, cancellationToken);

    public void Add(Budget budget) => db.Budgets.Add(budget);

    public void AddUsage(BudgetUsage usage) => db.Usages.Add(usage);

    public string ETagOf(Budget budget) => db.GetETag(budget);

    public void ApplyIfMatch(Budget budget, string? ifMatch) => db.ApplyIfMatch(budget, ifMatch);

    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
}
