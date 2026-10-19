using System.Text.Json.Nodes;

namespace Saaskit.Tenancy.Application;

/// <summary>One setting of a module, for the screens that edit it.</summary>
public sealed record SettingInfo(string Name, bool TenantEditable, object? Default, IReadOnlyList<string>? AllowedValues);

/// <summary>A module's settings for a tenant: the effective values, what the tenant overrides, and what can be changed.</summary>
public sealed record SettingsView(string ModuleKey, JsonObject Values, JsonObject Overrides, IReadOnlyList<SettingInfo> Settings);

/// <summary>A feature flag for a tenant: its default, the tenant's own choice (if any) and the result.</summary>
public sealed record FeatureView(string Key, string ModuleKey, string Description, bool Default, bool? Override, bool Effective);

/// <summary>What the signed-in frontend needs: branding, every flag's effective value, and the values of settings exposed to the UI.</summary>
public sealed record UiSettings(JsonObject Branding, IReadOnlyDictionary<string, bool> Features, IReadOnlyDictionary<string, JsonObject> Values);

/// <summary>
/// A tenant's settings and feature flags (rungs 1 and 2 of the customization ladder). Platform operators change any of them; a
/// tenant's own admin changes the ones marked <c>[TenantEditable]</c>. Every change is audited.
/// </summary>
public interface ITenantCustomization
{
    Task<UiSettings> GetUiSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SettingsView>> GetAllSettingsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<SettingsView> GetSettingsAsync(Guid tenantId, string moduleKey, CancellationToken cancellationToken = default);

    /// <summary>Sets the given overrides (a <c>null</c> value removes one). A tenant admin may change only editable settings.</summary>
    Task<SettingsView> SetSettingsAsync(Guid tenantId, string moduleKey, JsonObject changes, bool tenantAdmin, string actor, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureView>> GetFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Switches a flag on or off for the tenant; <c>null</c> goes back to the flag's default.</summary>
    Task<FeatureView> SetFeatureAsync(Guid tenantId, string featureKey, bool? enabled, string actor, CancellationToken cancellationToken = default);
}
