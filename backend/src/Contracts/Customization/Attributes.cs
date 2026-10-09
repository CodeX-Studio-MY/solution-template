namespace Acme.Contracts;

/// <summary>
/// Marks a record as a module's per-tenant settings (rung 1 of the customization ladder). Values are data in the catalog: a tenant
/// stores only what differs from the record's defaults, so a new tenant works with zero configuration. Settings types live here
/// (not in the module) because Tenancy edits them and the module reads them, and neither may reference the other.
/// </summary>
/// <param name="moduleKey">The module that owns the settings (see <see cref="ModuleKeys"/>).</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantSettingsAttribute(string moduleKey) : Attribute
{
    public string ModuleKey { get; } = moduleKey;

    /// <summary>Whether the signed-in frontend may read the values (branding, for example). Never for anything a user shouldn't see.</summary>
    public bool ExposedToUi { get; init; }
}

/// <summary>A setting that a tenant's own admin may change. Everything else is changed by platform operators only.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class TenantEditableAttribute : Attribute;

/// <summary>
/// Declares a feature flag (rung 2). Put it on a <c>const string</c> holding the flag key, so the key is written once and the
/// frontend's flag type is generated from it.
/// </summary>
/// <param name="moduleKey">The module the flag belongs to (see <see cref="ModuleKeys"/>).</param>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class FeatureFlagAttribute(string moduleKey) : Attribute
{
    public string ModuleKey { get; } = moduleKey;

    /// <summary>The value for a tenant that has no override. Every flag has a default.</summary>
    public bool DefaultEnabled { get; init; }

    public string Description { get; init; } = "";
}
