using System.Text.Json;
using System.Text.Json.Nodes;
using Farm360.Contracts;
using Farm360.Core.Catalog;
using Farm360.Core.Catalog.Entities;
using Farm360.Core.Customization;
using Farm360.Core.Errors;
using Farm360.Tenancy.Application;
using Farm360.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Farm360.Tenancy.Infrastructure;

internal sealed class TenantCustomization(
    CatalogDbContext catalog,
    TenancyCatalogDbContext tenancy,
    ITenantCustomizationCache cache,
    TimeProvider time) : ITenantCustomization
{
    public async Task<UiSettings> GetUiSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stored = await StoredAsync(tenantId, cancellationToken);
        var branding = Effective(stored, ModuleKeys.TenantSettings);
        var values = CustomizationRegistry.Settings
            .Where(d => d.ExposedToUi)
            .ToDictionary(d => d.ModuleKey, d => Effective(stored, d.ModuleKey), StringComparer.Ordinal);
        var features = await GetFeaturesAsync(tenantId, cancellationToken);
        return new UiSettings(branding, features.ToDictionary(f => f.Key, f => f.Effective, StringComparer.Ordinal), values);
    }

    public async Task<IReadOnlyList<SettingsView>> GetAllSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var stored = await StoredAsync(tenantId, cancellationToken);
        return [.. CustomizationRegistry.Settings.OrderBy(d => d.ModuleKey, StringComparer.Ordinal).Select(d => View(d, stored))];
    }

    public async Task<SettingsView> GetSettingsAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default) =>
        View(Descriptor(moduleKey), await StoredAsync(tenantId, cancellationToken));

    public async Task<SettingsView> SetSettingsAsync(Guid tenantId, string moduleKey, JsonObject changes, bool tenantAdmin, string actor, CancellationToken cancellationToken = default)
    {
        var descriptor = Descriptor(moduleKey);
        var row = await catalog.TenantSettings.SingleOrDefaultAsync(s => s.TenantId == tenantId && s.ModuleKey == moduleKey, cancellationToken);
        var overrides = row is null ? new JsonObject() : JsonNode.Parse(row.Json)?.AsObject() ?? new JsonObject();

        // A null value takes the override away (the setting goes back to its default); anything else replaces it.
        foreach (var (name, value) in changes)
        {
            var canonical = JsonNamingPolicy.CamelCase.ConvertName(descriptor.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Name ?? name);
            overrides.Remove(canonical);
            if (value is not null)
                overrides[canonical] = value.DeepClone();
        }

        var problems = CustomizationRegistry.Validate(descriptor, overrides, tenantEditableOnly: tenantAdmin);
        if (problems.Count > 0)
            throw new ValidationException([.. problems.Select(p => new FieldError(moduleKey, "validation.invalid", p))]);

        var json = overrides.ToJsonString();
        if (json.Length > TenantSetting.MaxJsonLength)
            throw new ValidationException([new FieldError(moduleKey, "validation.maxLength", moduleKey, TenantSetting.MaxJsonLength)]);
        var now = time.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            catalog.TenantSettings.Add(new TenantSetting { TenantId = tenantId, ModuleKey = moduleKey, Json = json, UpdatedOn = now, UpdatedBy = actor });
        }
        else
        {
            row.Json = json;
            row.UpdatedOn = now;
            row.UpdatedBy = actor;
        }
        tenancy.AuditLog.Add(new TenancyAuditEntry(now, tenantId, "settings-changed", actor, JsonSerializer.Serialize(new { module = moduleKey, names = changes.Select(c => c.Key).ToArray() })));
        await catalog.SaveChangesAsync(cancellationToken);
        await tenancy.SaveChangesAsync(cancellationToken);
        cache.Invalidate(tenantId);
        return await GetSettingsAsync(tenantId, moduleKey, cancellationToken);
    }

    public async Task<IReadOnlyList<FeatureView>> GetFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var overrides = await catalog.TenantFeatures.AsNoTracking().Where(f => f.TenantId == tenantId).ToDictionaryAsync(f => f.FeatureKey, f => f.Enabled, cancellationToken);
        return [.. CustomizationRegistry.Features.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => ToView(f, overrides))];
    }

    public async Task<FeatureView> SetFeatureAsync(Guid tenantId, string featureKey, bool? enabled, string actor, CancellationToken cancellationToken = default)
    {
        var feature = CustomizationRegistry.FindFeature(featureKey) ?? throw new NotFoundException(TenancyErrors.UnknownFeature);
        var row = await catalog.TenantFeatures.SingleOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, cancellationToken);
        var now = time.GetUtcNow().UtcDateTime;
        if (enabled is null)
        {
            if (row is not null)
                catalog.TenantFeatures.Remove(row);
        }
        else if (row is null)
        {
            catalog.TenantFeatures.Add(new TenantFeature { TenantId = tenantId, FeatureKey = featureKey, Enabled = enabled.Value, UpdatedOn = now, UpdatedBy = actor });
        }
        else
        {
            row.Enabled = enabled.Value;
            row.UpdatedOn = now;
            row.UpdatedBy = actor;
        }
        tenancy.AuditLog.Add(new TenancyAuditEntry(now, tenantId, "feature-changed", actor, JsonSerializer.Serialize(new { feature = featureKey, enabled })));
        await catalog.SaveChangesAsync(cancellationToken);
        await tenancy.SaveChangesAsync(cancellationToken);
        cache.Invalidate(tenantId);
        var overrides = await catalog.TenantFeatures.AsNoTracking().Where(f => f.TenantId == tenantId).ToDictionaryAsync(f => f.FeatureKey, f => f.Enabled, cancellationToken);
        return ToView(feature, overrides);
    }

    private static FeatureView ToView(FeatureDescriptor feature, Dictionary<string, bool> overrides)
    {
        bool? own = overrides.TryGetValue(feature.Key, out var value) ? value : null;
        return new FeatureView(feature.Key, feature.ModuleKey, feature.Description, feature.DefaultEnabled, own, own ?? feature.DefaultEnabled);
    }

    private async Task<Dictionary<string, string>> StoredAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await catalog.TenantSettings.AsNoTracking().Where(s => s.TenantId == tenantId).ToDictionaryAsync(s => s.ModuleKey, s => s.Json, cancellationToken);

    private static SettingsDescriptor Descriptor(string moduleKey) =>
        CustomizationRegistry.FindSettings(moduleKey) ?? throw new NotFoundException(TenancyErrors.UnknownSettings);

    /// <summary>The record's defaults with the tenant's overrides on top, as JSON.</summary>
    private static JsonObject Effective(Dictionary<string, string> stored, string moduleKey)
    {
        var descriptor = CustomizationRegistry.FindSettings(moduleKey);
        if (descriptor is null)
            return new JsonObject();
        var defaults = JsonSerializer.SerializeToNode(Activator.CreateInstance(descriptor.Type), descriptor.Type, CustomizationRegistry.Json)!.AsObject();
        if (stored.TryGetValue(moduleKey, out var json) && JsonNode.Parse(json) is JsonObject overrides)
        {
            foreach (var (name, value) in overrides)
                defaults[name] = value?.DeepClone();
        }
        return defaults;
    }

    private static SettingsView View(SettingsDescriptor descriptor, Dictionary<string, string> stored) => new(
        descriptor.ModuleKey,
        Effective(stored, descriptor.ModuleKey),
        stored.TryGetValue(descriptor.ModuleKey, out var json) && JsonNode.Parse(json) is JsonObject overrides ? overrides : new JsonObject(),
        [.. descriptor.Properties.Select(p => new SettingInfo(JsonNamingPolicy.CamelCase.ConvertName(p.Name), p.TenantEditable, p.Default, p.AllowedValues))]);
}
