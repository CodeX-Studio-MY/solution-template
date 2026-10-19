using Saaskit.Core.Tenancy;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace Saaskit.Core.Customization;

/// <summary>The tenant's flag overrides, as the catalog holds them. Implemented by <c>Core.Catalog</c>.</summary>
public interface ITenantFeatureStore
{
    /// <summary>The flags a tenant has switched on or off explicitly (key → value). Flags it hasn't touched are absent.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Whether a behaviour is on for the current tenant (rung 2). A feature that is off should behave as if it doesn't exist:
/// return 404, never "forbidden". Evaluated through an OpenFeature provider, so a hosted flag service can replace the catalog later.
/// </summary>
public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default);
}

/// <summary>The OpenFeature provider backed by the catalog: a tenant's explicit value, else the flag's declared default.</summary>
public sealed class CatalogFeatureProvider(ITenantFeatureStore store) : FeatureProvider
{
    /// <summary>The evaluation-context key that carries the tenant's ID.</summary>
    public const string TenantKey = "tenantId";

    public override Metadata GetMetadata() => new("catalog");

    public override async Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default)
    {
        var feature = CustomizationRegistry.FindFeature(flagKey);
        if (feature is null)
            return new ResolutionDetails<bool>(flagKey, defaultValue, ErrorType.FlagNotFound, Reason.Error);

        if (context is not null && context.TryGetValue(TenantKey, out var tenant) && Guid.TryParse(tenant?.AsString, out var tenantId))
        {
            var overrides = await store.GetAsync(tenantId, cancellationToken);
            if (overrides.TryGetValue(flagKey, out var value))
                return new ResolutionDetails<bool>(flagKey, value, reason: Reason.TargetingMatch);
        }
        return new ResolutionDetails<bool>(flagKey, feature.DefaultEnabled, reason: Reason.Default);
    }

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<string>(flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error));

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<int>(flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error));

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(string flagKey, double defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<double>(flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error));

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ResolutionDetails<Value>(flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error));
}

internal sealed class OpenFeatureFlags(FeatureProvider provider, ITenantContext tenant, Saaskit.Core.Security.ICurrentPrincipal principal) : IFeatureFlags
{
    public async Task<bool> IsEnabledAsync(string featureKey, CancellationToken cancellationToken = default)
    {
        var context = EvaluationContext.Builder()
            .Set(CatalogFeatureProvider.TenantKey, tenant.Tenant.Id.ToString())
            .Set("principalId", principal.Id ?? "")
            .Build();
        var result = await provider.ResolveBooleanValueAsync(featureKey, false, context, cancellationToken);
        return result.Value;
    }
}
