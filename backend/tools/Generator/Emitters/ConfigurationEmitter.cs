using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using Farm360.Core.Configuration;

namespace Farm360.Generator.Emitters;

/// <summary>
/// From the typed options records in Core (the single source of truth for runtime configuration):
/// deploy/.env.example (keys only), docs/configuration.md, and the AppHost's environment variable names.
/// </summary>
internal static class ConfigurationEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var sections = Sections().ToList();
        EmitEnvExample(files, sections);
        EmitDocs(files, sections);
        EmitAppHostKeys(platform, files, sections);
    }

    internal static IEnumerable<Section> Sections() =>
        typeof(OptionsSectionAttribute).Assembly.GetTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<OptionsSectionAttribute>()))
            .Where(x => x.Attribute is not null)
            .OrderBy(x => x.Attribute!.Name, StringComparer.Ordinal)
            .Select(x => new Section(x.Attribute!.Name, x.Attribute.Description, Settings(x.Type).ToList()));

    private static IEnumerable<Setting> Settings(Type type)
    {
        var defaults = Activator.CreateInstance(type);
        // Only bindable (settable) properties are configuration; computed ones (for example Issuer) are derived.
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var setting = property.GetCustomAttribute<SettingAttribute>()
                ?? throw new InvalidOperationException($"{type.Name}.{property.Name} needs a [Setting] description.");
            var isMap = property.PropertyType.IsGenericType
                && (property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>) || property.PropertyType.GetGenericTypeDefinition() == typeof(List<>));
            var isList = isMap && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>);
            var value = property.GetValue(defaults);
            var hasDefault = value switch
            {
                null => false,
                string s => s.Length > 0,
                ICollection c => c.Count > 0,
                _ => true,
            };
            var constraints = new List<string>();
            if (property.GetCustomAttribute<AllowedValuesAttribute>() is { } allowed)
                constraints.Add("one of " + string.Join(", ", allowed.Values.Select(v => $"`{v}`")));
            if (property.GetCustomAttribute<RangeAttribute>() is { } range)
                constraints.Add($"{range.Minimum}–{range.Maximum}");
            if (property.GetCustomAttribute<RegularExpressionAttribute>() is { } regex)
                constraints.Add($"matches `{regex.Pattern}`");
            yield return new Setting(
                property.Name,
                isMap,
                TypeName(property.PropertyType),
                property.GetCustomAttribute<RequiredAttribute>() is not null,
                property.GetCustomAttribute<SecretAttribute>() is not null,
                hasDefault ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null,
                setting.Description,
                isList ? "0" : setting.Example,
                string.Join("; ", constraints));
        }
    }

    private static void EmitEnvExample(GeneratedFiles files, List<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Every runtime configuration key, with no values. Copy to .env (git-ignored) for local use without Aspire.");
        sb.AppendLine("# Secrets come from the secret store or managed identity in the cloud; never commit values.");
        foreach (var section in sections)
        {
            sb.AppendLine();
            sb.AppendLine($"# --- {section.Name}: {section.Description}");
            foreach (var setting in section.Settings)
            {
                sb.AppendLine($"# {setting.Description}{(setting.Required ? " (required)" : "")}{(setting.Secret ? " (secret)" : "")}");
                sb.AppendLine($"{section.EnvName(setting)}=");
            }
        }
        files.AddRaw("deploy/.env.example", $"# {GeneratedFiles.Notice.Replace("platform.yaml", "the options records in backend/src/Core/Configuration", StringComparison.Ordinal)}\n{sb}");
    }

    private static void EmitDocs(GeneratedFiles files, List<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Configuration reference");
        sb.AppendLine();
        sb.AppendLine("Generated from the typed options records in `backend/src/Core/Configuration` (the single source of truth for runtime configuration).");
        sb.AppendLine("Values differ between environments only through environment variables and the secret store; there are no per-environment settings files.");
        sb.AppendLine("Hosts validate this schema at startup and refuse to start on missing or invalid values.");
        sb.AppendLine();
        sb.AppendLine("Standard variables read by libraries directly (for example `OTEL_EXPORTER_OTLP_ENDPOINT`, `ASPNETCORE_URLS`) are not listed here.");
        foreach (var section in sections)
        {
            sb.AppendLine();
            sb.AppendLine($"## {section.Name}");
            sb.AppendLine();
            sb.AppendLine(section.Description);
            sb.AppendLine();
            sb.AppendLine("| Environment variable | Type | Required | Default | Description |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var setting in section.Settings)
            {
                var description = setting.Description
                    + (setting.Secret ? " **Secret.**" : "")
                    + (setting.Constraints.Length > 0 ? $" Allowed: {setting.Constraints}." : "")
                    + (setting.Example is null ? "" : $" Example: `{setting.Example}`.");
                sb.AppendLine($"| `{section.EnvName(setting)}` | {setting.Type} | {(setting.Required ? "yes" : "no")} | {(setting.Default is null ? "" : $"`{setting.Default}`")} | {description} |");
            }
        }
        files.AddRaw("docs/configuration.md", $"<!-- {GeneratedFiles.Notice.Replace("platform.yaml", "the options records in backend/src/Core/Configuration", StringComparison.Ordinal)} -->\n{sb}");
    }

    private static void EmitAppHostKeys(PlatformDefinition platform, GeneratedFiles files, List<Section> sections)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"namespace {platform.ProductPascal}.AppHost;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Environment variable names from the configuration schema in Core.</summary>");
        sb.AppendLine("internal static class ConfigKeys");
        sb.AppendLine("{");
        foreach (var section in sections)
        {
            sb.AppendLine($"    public static class {section.Name}");
            sb.AppendLine("    {");
            foreach (var setting in section.Settings)
            {
                if (setting.IsMap)
                    sb.AppendLine($"        public static string {setting.Name}(object key) => $\"{section.Name}__{setting.Name}__{{key}}\";");
                else
                    sb.AppendLine($"        public const string {setting.Name} = \"{section.Name}__{setting.Name}\";");
            }
            sb.AppendLine("    }");
        }
        sb.AppendLine("}");
        files.Add("backend/src/AppHost/Generated/ConfigKeys.g.cs", "//", sb.ToString());
    }

    private static string TypeName(Type type) => type switch
    {
        _ when type == typeof(string) => "string",
        _ when type == typeof(int) => "integer",
        _ when type == typeof(bool) => "boolean",
        _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>) => "map of string",
        _ when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) => "list of string (one variable per index)",
        _ => type.Name,
    };

    internal sealed record Section(string Name, string Description, List<Setting> Settings)
    {
        /// <summary><c>Database__Provider</c>; maps show their example key, <c>Database__Servers__default</c>.</summary>
        public string EnvName(Setting setting) =>
            setting.IsMap ? $"{Name}__{setting.Name}__{setting.Example ?? "default"}" : $"{Name}__{setting.Name}";
    }

    internal sealed record Setting(string Name, bool IsMap, string Type, bool Required, bool Secret, string? Default, string Description, string? Example, string Constraints);
}
