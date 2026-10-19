using System.Globalization;
using System.Text.Json;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Core.Customization;

public enum CustomFieldType
{
    Text = 1,
    Number = 2,
    Date = 3,
    List = 4,
}

/// <summary>
/// A field a tenant added to one of a module's entities (rung 4). The definitions are rows in the module's own schema; the
/// values live in one JSON column on the entity. The server validates every write against the definitions.
/// </summary>
public sealed class CustomFieldDefinition : BaseEntity
{
    public const int MaxKeyLength = 60;

    private CustomFieldDefinition()
    {
    }

    public CustomFieldDefinition(string entity, string key, string label, CustomFieldType type, bool required, IEnumerable<string>? options, int displayOrder)
    {
        Entity = entity;
        Key = key;
        Update(label, type, required, options, displayOrder);
    }

    /// <summary>The entity type the field belongs to (for example <c>goods-receipt</c>).</summary>
    public string Entity { get; private set; } = "";

    /// <summary>Lowercase letters, digits and hyphens; unique per entity. This is the key in the entity's <c>customFields</c>.</summary>
    public string Key { get; private set; } = "";

    /// <summary>An i18n key, or plain text.</summary>
    public string Label { get; private set; } = "";

    public CustomFieldType Type { get; private set; }

    public bool Required { get; private set; }

    /// <summary>The allowed values of a list field, as a JSON array.</summary>
    public string OptionsJson { get; private set; } = "[]";

    public int DisplayOrder { get; private set; }

    public IReadOnlyList<string> Options => JsonSerializer.Deserialize<List<string>>(OptionsJson, CustomizationRegistry.Json) ?? [];

    public void Update(string label, CustomFieldType type, bool required, IEnumerable<string>? options, int displayOrder)
    {
        Label = label;
        Type = type;
        Required = required;
        OptionsJson = JsonSerializer.Serialize(type == CustomFieldType.List ? options?.Distinct(StringComparer.Ordinal).ToList() ?? [] : [], CustomizationRegistry.Json);
        DisplayOrder = displayOrder;
    }
}

/// <summary>Checks an entity's custom field values against its definitions.</summary>
public static class CustomFieldValidator
{
    public const int MaxTextLength = 500;

    /// <summary>
    /// Returns the cleaned values (only known keys, trimmed) and adds a field error for each problem: a required field that is empty,
    /// a value of the wrong type, a list value that isn't an option. Unknown keys are rejected, never silently stored.
    /// </summary>
    public static Dictionary<string, string> Validate(IReadOnlyList<CustomFieldDefinition> definitions, IReadOnlyDictionary<string, string?>? values, ValidationBuilder errors)
    {
        var clean = new Dictionary<string, string>(StringComparer.Ordinal);
        values ??= new Dictionary<string, string?>();

        foreach (var key in values.Keys.Where(k => definitions.All(d => d.Key != k)))
            errors.Add($"customFields.{key}", "validation.unknownField", key);

        foreach (var definition in definitions)
        {
            var field = $"customFields.{definition.Key}";
            var text = values.GetValueOrDefault(definition.Key);
            if (string.IsNullOrWhiteSpace(text))
            {
                if (definition.Required)
                    errors.Add(field, "validation.required", definition.Label);
                continue;
            }
            text = text.Trim();
            switch (definition.Type)
            {
                case CustomFieldType.Number when !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                    errors.Add(field, "validation.number", definition.Label);
                    continue;
                case CustomFieldType.Date when !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                    errors.Add(field, "validation.date", definition.Label);
                    continue;
                case CustomFieldType.List when !definition.Options.Contains(text, StringComparer.Ordinal):
                    errors.Add(field, "validation.notAnOption", definition.Label);
                    continue;
                case CustomFieldType.Text or CustomFieldType.Number when text.Length > MaxTextLength:
                    errors.Add(field, "validation.maxLength", definition.Label, MaxTextLength);
                    continue;
            }
            clean[definition.Key] = text;
        }
        return clean;
    }
}

public static class CustomFieldModel
{
    /// <summary>Maps <see cref="CustomFieldDefinition"/> into a module's schema. Call from <c>ConfigureModel</c>.</summary>
    public static void ConfigureCustomFieldDefinitions(this ModelBuilder modelBuilder, string tableName = "CustomFieldDefinitions")
    {
        modelBuilder.Entity<CustomFieldDefinition>(definition =>
        {
            definition.ToTable(tableName);
            definition.Property(d => d.Entity).HasMaxLength(60).IsUnicode(false);
            definition.Property(d => d.Key).HasMaxLength(CustomFieldDefinition.MaxKeyLength).IsUnicode(false);
            definition.Property(d => d.Label).HasMaxLength(200);
            definition.Property(d => d.Type).HasConversion<string>().HasMaxLength(20);
            definition.Property(d => d.OptionsJson).HasMaxLength(4000);
            definition.Ignore(d => d.Options);
            definition.HasIndex(d => new { d.Entity, d.Key }).IsUnique();
        });
    }
}
