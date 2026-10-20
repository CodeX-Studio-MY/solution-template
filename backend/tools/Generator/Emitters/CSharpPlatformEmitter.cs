using System.Text;

namespace Farm360.Generator.Emitters;

/// <summary>backend/src/Core/Generated/Platform.g.cs: module keys, roles, plans, services and product parameters.</summary>
internal static class CSharpPlatformEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var ns = platform.ProductPascal;
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {ns}.Core.Platform;");
        sb.AppendLine();

        sb.AppendLine("/// <summary>Product parameters from platform.yaml.</summary>");
        sb.AppendLine("public static class PlatformInfo");
        sb.AppendLine("{");
        sb.AppendLine($"    public const string Product = {Naming.Quote(platform.Product)};");
        sb.AppendLine($"    public const string Org = {Naming.Quote(platform.Org)};");
        sb.AppendLine($"    public const string Cloud = {Naming.Quote(platform.Cloud)};");
        sb.AppendLine($"    public const string DefaultTimeZone = {Naming.Quote(platform.Defaults.Timezone)};");
        sb.AppendLine($"    public const string DefaultCurrency = {Naming.Quote(platform.Defaults.Currency)};");
        sb.AppendLine($"    public static readonly IReadOnlyList<string> DefaultLocales = [{string.Join(", ", platform.Defaults.Locales.Select(Naming.Quote))}];");
        sb.AppendLine($"    public static readonly IReadOnlyList<string> DatabaseProviders = [{string.Join(", ", platform.Database.Providers.Select(Naming.Quote))}];");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("/// <summary>Access module keys: what roles and plans apply to.</summary>");
        sb.AppendLine("public static class Modules");
        sb.AppendLine("{");
        foreach (var module in platform.Modules)
            sb.AppendLine($"    public const string {module.Pascal} = {Naming.Quote(module.Key)};");
        sb.AppendLine();
        sb.AppendLine("    public static readonly IReadOnlyList<ModuleInfo> All =");
        sb.AppendLine("    [");
        foreach (var module in platform.Modules)
            sb.AppendLine($"        new({module.Pascal}, {Naming.Quote(module.Name)}, new Guid(\"{Naming.StableGuid("module:" + module.Key)}\"), [{string.Join(", ", module.Plans.Select(Naming.Quote))}]),");
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("/// <summary>Roles, lowest to highest. A higher role includes everything a lower one can do.</summary>");
        sb.AppendLine("public enum Role");
        sb.AppendLine("{");
        for (var i = 0; i < platform.Roles.Count; i++)
            sb.AppendLine($"    {Naming.Pascal(platform.Roles[i])} = {i + 1},");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("/// <summary>Role names as they appear in tokens.</summary>");
        sb.AppendLine("public static class Roles");
        sb.AppendLine("{");
        foreach (var role in platform.Roles)
            sb.AppendLine($"    public const string {Naming.Pascal(role)} = {Naming.Quote(role)};");
        sb.AppendLine();
        sb.AppendLine("    public static string Name(Role role) => role switch");
        sb.AppendLine("    {");
        foreach (var role in platform.Roles)
            sb.AppendLine($"        Role.{Naming.Pascal(role)} => {Naming.Pascal(role)},");
        sb.AppendLine("        _ => throw new ArgumentOutOfRangeException(nameof(role)),");
        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    public static bool TryParse(string? name, out Role role)");
        sb.AppendLine("    {");
        sb.AppendLine("        switch (name)");
        sb.AppendLine("        {");
        foreach (var role in platform.Roles)
        {
            sb.AppendLine($"            case {Naming.Pascal(role)}:");
            sb.AppendLine($"                role = Role.{Naming.Pascal(role)};");
            sb.AppendLine("                return true;");
        }
        sb.AppendLine("            default:");
        sb.AppendLine("                role = default;");
        sb.AppendLine("                return false;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("public static class Plans");
        sb.AppendLine("{");
        foreach (var plan in platform.Plans)
            sb.AppendLine($"    public const string {Naming.Pascal(plan)} = {Naming.Quote(plan)};");
        sb.AppendLine();
        sb.AppendLine($"    public static readonly IReadOnlyList<string> All = [{string.Join(", ", platform.Plans.Select(Naming.Pascal))}];");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("/// <summary>Services: what runs and is routed at /api/{key}/*.</summary>");
        sb.AppendLine("public static class PlatformServices");
        sb.AppendLine("{");
        foreach (var service in platform.Services)
        {
            var owns = string.Join(", ", service.Owns.Select(k => $"Modules.{Naming.Pascal(k)}"));
            sb.AppendLine($"    public static readonly PlatformService {service.Pascal} = new({Naming.Quote(service.Key)}, {service.Port}, {Naming.Quote($"{platform.Product}:{service.Key}")}, [{owns}], UsesTenantDatabase: {Bool(service.UsesTenantDatabase)}, UsesCatalogDatabase: {Bool(service.UsesCatalogDatabase)});");
        }
        sb.AppendLine();
        sb.AppendLine($"    public static readonly IReadOnlyList<PlatformService> All = [{string.Join(", ", platform.Services.Select(s => s.Pascal))}];");
        sb.AppendLine("}");

        files.Add("backend/src/Core/Generated/Platform.g.cs", "//", sb.ToString());

        // Shared contract types (settings, flags) name the module they belong to, and Contracts references nothing, so it gets its own copy.
        var keys = new StringBuilder();
        keys.AppendLine("#nullable enable");
        keys.AppendLine($"namespace {ns}.Contracts;");
        keys.AppendLine();
        keys.AppendLine("/// <summary>Access module keys from platform.yaml, for contract types that name their module.</summary>");
        keys.AppendLine("public static class ModuleKeys");
        keys.AppendLine("{");
        foreach (var module in platform.Modules)
            keys.AppendLine($"    public const string {module.Pascal} = {Naming.Quote(module.Key)};");
        keys.AppendLine("}");
        files.Add("backend/src/Contracts/Generated/ModuleKeys.g.cs", "//", keys.ToString());
    }

    private static string Bool(bool value) => value ? "true" : "false";
}
