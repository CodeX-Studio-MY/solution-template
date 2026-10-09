using System.Text;

namespace Acme.Generator.Emitters;

/// <summary>backend/src/AppHost/Generated/PlatformResources.g.cs: one Aspire project resource per service.</summary>
internal static class AppHostEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {platform.ProductPascal}.AppHost;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class PlatformResources");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Adds every service host from platform.yaml, wired to the shared infrastructure.</summary>");
        sb.AppendLine("    public static IReadOnlyList<IResourceBuilder<ProjectResource>> AddServices(IDistributedApplicationBuilder builder, PlatformInfrastructure infrastructure) =>");
        sb.AppendLine("    [");
        foreach (var service in platform.Services)
        {
            sb.AppendLine($"        infrastructure.ConfigureService(builder");
            sb.AppendLine($"            .AddProject({Naming.Quote(service.Key)}, \"../Hosts/{service.Pascal}.Host/{service.Pascal}.Host.csproj\")");
            sb.AppendLine($"            .WithHttpEndpoint(port: {service.Port}, name: \"http\")),");
        }
        sb.AppendLine("    ];");
        sb.AppendLine("}");
        files.Add("backend/src/AppHost/Generated/PlatformResources.g.cs", "//", sb.ToString());
    }
}
