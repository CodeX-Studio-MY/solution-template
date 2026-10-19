using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Saaskit.Contracts;

namespace Saaskit.Core.Customization;

/// <summary>One property of a module's settings record, as the admin screens and the validator see it.</summary>
public sealed record SettingProperty(string Name, Type Type, bool TenantEditable, object? Default, IReadOnlyList<string>? AllowedValues);

/// <summary>A module's settings record and where it came from.</summary>
public sealed record SettingsDescriptor(string ModuleKey, Type Type, bool ExposedToUi, IReadOnlyList<SettingProperty> Properties);

/// <summary>A declared feature flag.</summary>
public sealed record FeatureDescriptor(string Key, string ModuleKey, bool DefaultEnabled, string Description);

/// <summary>
/// Every settings record and feature flag declared in <c>Contracts</c>, found by attribute (like integration events). Tenancy edits
/// them and modules read them, and neither references the other: this is what they share.
/// </summary>
public static class CustomizationRegistry
{
    private static readonly FrozenDictionary<string, SettingsDescriptor> SettingsByModule = DiscoverSettings();
    private static readonly FrozenDictionary<string, FeatureDescriptor> FeaturesByKey = DiscoverFeatures();

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static IReadOnlyCollection<SettingsDescriptor> Settings => SettingsByModule.Values;

    public static IReadOnlyCollection<FeatureDescriptor> Features => FeaturesByKey.Values;

    public static SettingsDescriptor? FindSettings(string moduleKey) => SettingsByModule.GetValueOrDefault(moduleKey);

    public static SettingsDescriptor SettingsFor<T>() =>
        SettingsByModule.Values.SingleOrDefault(d => d.Type == typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).FullName} is not a settings record. Declare it in Contracts with [TenantSettings].");

    public static FeatureDescriptor? FindFeature(string key) => FeaturesByKey.GetValueOrDefault(key);

    /// <summary>Reads a tenant's stored overrides on top of the record's defaults. Unknown or invalid stored values fall back to the default.</summary>
    public static T Resolve<T>(string? storedJson)
        where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(storedJson))
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(storedJson, Json) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    /// <summary>
    /// Checks the overrides a caller wants to store: only known properties, only editable ones when the caller is a tenant admin,
    /// and every value valid for the record (ranges, allowed values, patterns). Returns the problems; empty means valid.
    /// </summary>
    public static IReadOnlyList<string> Validate(SettingsDescriptor descriptor, JsonObject overrides, bool tenantEditableOnly)
    {
        var problems = new List<string>();
        foreach (var (name, _) in overrides)
        {
            var property = descriptor.Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
                problems.Add($"'{name}' is not a setting of {descriptor.ModuleKey}.");
            else if (tenantEditableOnly && !property.TenantEditable)
                problems.Add($"'{name}' can only be changed by a platform operator.");
        }
        if (problems.Count > 0)
            return problems;

        object? instance;
        try
        {
            instance = overrides.Deserialize(descriptor.Type, Json);
        }
        catch (JsonException ex)
        {
            return [$"The values are not valid: {ex.Message}"];
        }
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance!, new ValidationContext(instance!), results, validateAllProperties: true);
        problems.AddRange(results.Select(r => r.ErrorMessage ?? "Invalid value."));
        return problems;
    }

    /// <summary>Contracts, and any loaded assembly that opts in with <see cref="DeclaresCustomizationAttribute"/> (tests).</summary>
    private static IEnumerable<Type> DeclaringTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.IsDefined(typeof(DeclaresCustomizationAttribute)))
            .Prepend(typeof(TenantSettingsAttribute).Assembly)
            .Distinct()
            .SelectMany(a => a.GetTypes());

    private static FrozenDictionary<string, SettingsDescriptor> DiscoverSettings() =>
        DeclaringTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<TenantSettingsAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToFrozenDictionary(
                x => x.Attribute!.ModuleKey,
                x => new SettingsDescriptor(x.Attribute!.ModuleKey, x.Type, x.Attribute.ExposedToUi, DescribeProperties(x.Type)),
                StringComparer.Ordinal);

    private static List<SettingProperty> DescribeProperties(Type type)
    {
        var defaults = Activator.CreateInstance(type);
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .Select(p => new SettingProperty(
                p.Name,
                p.PropertyType,
                p.GetCustomAttribute<TenantEditableAttribute>() is not null,
                p.GetValue(defaults),
                p.GetCustomAttribute<AllowedValuesAttribute>()?.Values.Select(v => v?.ToString() ?? "").ToList()))
            .ToList();
    }

    private static FrozenDictionary<string, FeatureDescriptor> DiscoverFeatures() =>
        DeclaringTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Select(f => (Field: f, Attribute: f.GetCustomAttribute<FeatureFlagAttribute>()))
            .Where(x => x.Attribute is not null && x.Field.IsLiteral)
            .Select(x => new FeatureDescriptor((string)x.Field.GetRawConstantValue()!, x.Attribute!.ModuleKey, x.Attribute.DefaultEnabled, x.Attribute.Description))
            .ToFrozenDictionary(f => f.Key, StringComparer.Ordinal);
}
