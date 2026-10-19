namespace Saaskit.Core.Configuration;

/// <summary>
/// Marks a record as one area of the runtime configuration schema, bound from <paramref name="name"/>.
/// <c>scripts/generate</c> reads these records to produce <c>.env.example</c> and <c>docs/configuration.md</c>,
/// and hosts validate them at startup. Defaults are written once, in the record.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OptionsSectionAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
}

/// <summary>The value is a secret: it comes from the secret store or a local <c>.env</c>, never from a committed file.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute;

/// <summary>Documents a configuration key for the generated configuration reference.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute(string description) : Attribute
{
    public string Description { get; } = description;

    /// <summary>An example value for documentation. Never a real secret.</summary>
    public string? Example { get; init; }
}
