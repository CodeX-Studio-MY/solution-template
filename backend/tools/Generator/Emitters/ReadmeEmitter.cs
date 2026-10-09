using System.Text;

namespace Acme.Generator.Emitters;

/// <summary>The services and modules tables in README.md, between generated-region markers.</summary>
internal static class ReadmeEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Service | Route | Local port | Owns modules | Databases |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var service in platform.Services)
            sb.AppendLine($"| `{service.Key}` | `/api/{service.Key}/*` | {service.Port} | {(service.Owns.Count == 0 ? "—" : string.Join(", ", service.Owns.Select(m => $"`{m}`")))} | {string.Join(", ", service.Databases)} |");
        sb.AppendLine();
        sb.AppendLine("| Module | Name | Plans |");
        sb.AppendLine("|---|---|---|");
        foreach (var module in platform.Modules)
            sb.AppendLine($"| `{module.Key}` | {module.Name} | {string.Join(", ", module.Plans)} |");
        files.ReplaceRegion("README.md", "modules", sb.ToString());
    }
}
