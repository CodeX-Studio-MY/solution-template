using Acme.PurchaseRequest.Application;
using Acme.PurchaseRequest.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.PurchaseRequest.Infrastructure;

/// <summary>The module's defaults. Per-tenant values replace this in milestone 5 (rung 1 of the customization ladder).</summary>
internal sealed class DefaultRequestSettings : IRequestSettings
{
    public Task<PurchaseRequestSettings> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new PurchaseRequestSettings());
}

/// <summary>Picks the tenant's approval strategy by key (keyed services); an unknown key falls back to the default.</summary>
internal sealed class ApprovalPolicyResolver(IServiceProvider services, IRequestSettings settings) : IApprovalPolicyResolver
{
    public async Task<IApprovalPolicy> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var key = (await settings.GetAsync(cancellationToken)).ApprovalPolicy;
        return services.GetKeyedService<IApprovalPolicy>(key) ?? services.GetRequiredKeyedService<IApprovalPolicy>(SingleApproverPolicy.Key);
    }
}
