using System.Text.RegularExpressions;
using Saaskit.Core.Platform;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Saaskit.Generator;

/// <summary>The parsed contents of <c>platform.yaml</c>, the single source of truth for the application's structure.</summary>
public sealed class PlatformDefinition
{
    public string Product { get; set; } = "";
    public string Org { get; set; } = "";
    public DefaultsDefinition Defaults { get; set; } = new();
    public string Cloud { get; set; } = "";
    public DatabaseDefinition Database { get; set; } = new();
    public List<string> Roles { get; set; } = [];
    public List<string> Plans { get; set; } = [];
    public List<ServiceDefinition> Services { get; set; } = [];
    public List<ModuleDefinition> Modules { get; set; } = [];

    public string ProductPascal => Naming.Pascal(Product);

    public static PlatformDefinition Load(string path)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        var definition = deserializer.Deserialize<PlatformDefinition>(File.ReadAllText(path))
            ?? throw new PlatformDefinitionException($"{path} is empty.");
        definition.Validate();
        definition.RequirePlatformParts();
        return definition;
    }

    /// <summary>The platform's own services and the modules they own: the platform's code and tests are built on them.</summary>
    internal static readonly (string Service, string[] Owns)[] PlatformParts =
    [
        ("identity", ["access-management"]),
        ("tenancy", ["tenant-settings", "billing"]),
        ("notification", []),
    ];

    /// <summary>
    /// Fails if a platform service or module was removed or moved. They can be renamed in the menu (<c>name</c>) and limited to some
    /// plans (<c>plans</c>), but not taken out: the platform's code depends on them.
    /// </summary>
    public void RequirePlatformParts()
    {
        var errors = new List<string>();
        foreach (var (service, owns) in PlatformParts)
        {
            var declared = Services.FirstOrDefault(s => s.Key == service);
            if (declared is null)
            {
                errors.Add($"service '{service}' is part of the platform and can't be removed.");
                continue;
            }
            foreach (var module in owns)
            {
                if (Modules.All(m => m.Key != module))
                    errors.Add($"module '{module}' is part of the platform and can't be removed. To keep it from customers, list only the plans that should have it (plans: [...]).");
                else if (!declared.Owns.Contains(module))
                    errors.Add($"module '{module}' must stay owned by service '{service}'.");
            }
        }
        if (errors.Count > 0)
            throw new PlatformDefinitionException("platform.yaml is invalid:\n  - " + string.Join("\n  - ", errors));
    }

    /// <summary>Fails on anything that would make the generated code ambiguous or unsafe.</summary>
    public void Validate()
    {
        var errors = new List<string>();

        if (!KeyRules.Pattern.IsMatch(Product))
            errors.Add($"product '{Product}' must match {KeyRules.Pattern}.");
        if (string.IsNullOrWhiteSpace(Org))
            errors.Add("org is required.");
        if (Cloud is not ("azure" or "aws"))
            errors.Add($"cloud '{Cloud}' must be 'azure' or 'aws'.");
        try { TimeZoneInfo.FindSystemTimeZoneById(Defaults.Timezone); }
        catch (TimeZoneNotFoundException) { errors.Add($"defaults.timezone '{Defaults.Timezone}' is not an IANA time zone ID."); }
        if (!Regex.IsMatch(Defaults.Currency, "^[A-Z]{3}$"))
            errors.Add($"defaults.currency '{Defaults.Currency}' must be an ISO 4217 code.");
        if (Defaults.Locales.Count == 0)
            errors.Add("defaults.locales must list at least one locale.");

        var knownProviders = new[] { "sqlserver", "postgresql" };
        if (Database.Providers.Count == 0)
            errors.Add("database.providers must list at least one provider.");
        foreach (var provider in Database.Providers.Where(p => !knownProviders.Contains(p)))
            errors.Add($"database.providers: '{provider}' is not supported (known: {string.Join(", ", knownProviders)}).");

        CheckList(errors, "roles", Roles);
        CheckList(errors, "plans", Plans);

        CheckKeys(errors, "service", Services.Select(s => s.Key));
        CheckKeys(errors, "module", Modules.Select(m => m.Key));

        foreach (var duplicate in Services.GroupBy(s => s.Port).Where(g => g.Count() > 1))
            errors.Add($"port {duplicate.Key} is used by more than one service: {string.Join(", ", duplicate.Select(s => s.Key))}.");

        var moduleKeys = Modules.Select(m => m.Key).ToHashSet();
        var owners = new Dictionary<string, string>();
        foreach (var service in Services)
        {
            if (service.Port is < 1024 or > 65535)
                errors.Add($"service '{service.Key}': port {service.Port} must be between 1024 and 65535.");
            foreach (var database in service.Databases.Where(d => d is not ("tenant" or "catalog")))
                errors.Add($"service '{service.Key}': database '{database}' must be 'tenant' or 'catalog'.");
            foreach (var owned in service.Owns)
            {
                if (!moduleKeys.Contains(owned))
                    errors.Add($"service '{service.Key}' owns '{owned}', which is not declared under modules.");
                else if (!owners.TryAdd(owned, service.Key))
                    errors.Add($"module '{owned}' is owned by both '{owners[owned]}' and '{service.Key}'.");
            }
        }

        foreach (var module in Modules)
        {
            if (!owners.ContainsKey(module.Key))
                errors.Add($"module '{module.Key}' is not owned by any service.");
            if (string.IsNullOrWhiteSpace(module.Name))
                errors.Add($"module '{module.Key}' needs a display name.");
            if (module.Plans.Count == 0)
                errors.Add($"module '{module.Key}' must be in at least one plan.");
            foreach (var plan in module.Plans.Where(p => !Plans.Contains(p)))
                errors.Add($"module '{module.Key}' uses plan '{plan}', which is not declared under plans.");
        }

        if (errors.Count > 0)
            throw new PlatformDefinitionException("platform.yaml is invalid:\n  - " + string.Join("\n  - ", errors));
    }

    public ServiceDefinition? OwnerOf(string moduleKey) => Services.FirstOrDefault(s => s.Owns.Contains(moduleKey));

    private static void CheckList(List<string> errors, string name, List<string> values)
    {
        if (values.Count == 0)
            errors.Add($"{name} must not be empty.");
        foreach (var value in values.Where(v => !KeyRules.Pattern.IsMatch(v)))
            errors.Add($"{name}: '{value}' must match {KeyRules.Pattern}.");
        foreach (var duplicate in values.GroupBy(v => v).Where(g => g.Count() > 1))
            errors.Add($"{name}: '{duplicate.Key}' is listed more than once.");
    }

    private static void CheckKeys(List<string> errors, string kind, IEnumerable<string> keys)
    {
        var list = keys.ToList();
        foreach (var key in list)
        {
            if (!KeyRules.Pattern.IsMatch(key))
                errors.Add($"{kind} key '{key}' must match {KeyRules.Pattern}.");
            if (KeyRules.Reserved.Contains(key))
                errors.Add($"{kind} key '{key}' is reserved ({string.Join(", ", KeyRules.Reserved)}).");
        }
        foreach (var duplicate in list.GroupBy(k => k).Where(g => g.Count() > 1))
            errors.Add($"{kind} key '{duplicate.Key}' is declared more than once.");
    }
}

public sealed class DefaultsDefinition
{
    public string Timezone { get; set; } = "";
    public string Currency { get; set; } = "";
    public List<string> Locales { get; set; } = [];
}

public sealed class DatabaseDefinition
{
    public List<string> Providers { get; set; } = [];
}

public sealed class ServiceDefinition
{
    public string Key { get; set; } = "";
    public int Port { get; set; }
    public List<string> Owns { get; set; } = [];

    /// <summary>Which databases the service's module keeps data in: <c>tenant</c> (default) and/or <c>catalog</c>.</summary>
    public List<string> Databases { get; set; } = ["tenant"];

    public string Pascal => Naming.Pascal(Key);
    public bool UsesTenantDatabase => Databases.Contains("tenant");
    public bool UsesCatalogDatabase => Databases.Contains("catalog");
}

public sealed class ModuleDefinition
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Plans { get; set; } = [];

    public string Pascal => Naming.Pascal(Key);
}

public sealed class PlatformDefinitionException(string message) : Exception(message);
