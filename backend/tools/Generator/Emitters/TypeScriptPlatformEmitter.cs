using System.Text;

namespace Acme.Generator.Emitters;

/// <summary>
/// frontend/packages/web-core/src/generated/platform.ts: the same module keys, roles, plans and defaults the backend has, so the
/// frontend never spells them by hand (<c>ModuleKey</c> and <c>Role</c> are unions of exactly what platform.yaml lists).
/// </summary>
internal static class TypeScriptPlatformEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        static string List(IEnumerable<string> items) => string.Join(", ", items.Select(i => $"\"{i}\""));

        var sb = new StringBuilder();
        sb.AppendLine($"export const productName = \"{platform.Product}\";");
        sb.AppendLine();
        sb.AppendLine("/** Roles, lowest to highest. \"At least this role\" is the position in this list. */");
        sb.AppendLine($"export const roles = [{List(platform.Roles)}] as const;");
        sb.AppendLine("export type Role = (typeof roles)[number];");
        sb.AppendLine();
        sb.AppendLine($"export const plans = [{List(platform.Plans)}] as const;");
        sb.AppendLine("export type Plan = (typeof plans)[number];");
        sb.AppendLine();
        sb.AppendLine("/** Access modules: what roles and plans apply to. */");
        sb.AppendLine($"export const moduleKeys = [{List(platform.Modules.Select(m => m.Key))}] as const;");
        sb.AppendLine("export type ModuleKey = (typeof moduleKeys)[number];");
        sb.AppendLine();
        sb.AppendLine("/** Backend services, routed at /api/{service-key}/*. */");
        sb.AppendLine($"export const serviceKeys = [{List(platform.Services.Select(s => s.Key))}] as const;");
        sb.AppendLine("export type ServiceKey = (typeof serviceKeys)[number];");
        sb.AppendLine();
        sb.AppendLine("export const platformDefaults = {");
        sb.AppendLine($"  timeZone: \"{platform.Defaults.Timezone}\",");
        sb.AppendLine($"  currency: \"{platform.Defaults.Currency}\",");
        sb.AppendLine($"  locales: [{List(platform.Defaults.Locales)}],");
        sb.AppendLine("} as const;");
        sb.AppendLine($"export type Locale = (typeof platformDefaults.locales)[number];");
        files.Add("frontend/packages/web-core/src/generated/platform.ts", "//", sb.ToString());
    }
}
