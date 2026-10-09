using Acme.Contracts;
using Acme.Core.Messaging;
using Acme.CostCenterBudget.Application;

namespace Acme.CostCenterBudget.Infrastructure;

/// <summary>
/// Rung 5 of the customization ladder: this module reacts to a purchase request being approved. purchase-request knows nothing
/// about it. Core delivers the event only to tenants entitled to this module, and the inbox makes a second delivery a no-op.
/// </summary>
internal sealed class UseBudgetOnApproval(BudgetService budgets) : IEventConsumer<PurchaseRequestApproved.V1>
{
    public Task HandleAsync(PurchaseRequestApproved.V1 integrationEvent, CancellationToken cancellationToken) =>
        budgets.UseAsync(integrationEvent.RequestId, integrationEvent.Number, integrationEvent.CostCenter, integrationEvent.Total, integrationEvent.Currency, cancellationToken);
}
