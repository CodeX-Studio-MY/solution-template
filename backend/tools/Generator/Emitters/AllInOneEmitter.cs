using System.Text;

namespace Acme.Generator.Emitters;

/// <summary>
/// AllInOne.Host runs every service's module in one process. Which modules exist is platform.yaml's business, so the project
/// references and the registration code are generated; the host's Program.cs has nothing to edit when a module is added.
/// </summary>
internal static class AllInOneEmitter
{
    private const string Dir = "backend/src/Hosts/AllInOne.Host/Generated";

    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var references = string.Join("\n", platform.Services.Select(s => $"    <ProjectReference Include=\"$(MSBuildThisFileDirectory)..\\..\\..\\Modules\\{s.Pascal}\\{s.Pascal}.csproj\" />"));
        files.AddXml($"{Dir}/ModuleProjects.g.props", $"""
            <Project>
              <ItemGroup>
            {references}
              </ItemGroup>
            </Project>
            """);

        var product = platform.ProductPascal;
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"using {product}.Core.Hosting;");
        sb.AppendLine();
        sb.AppendLine($"namespace {product}.AllInOne;");
        sb.AppendLine();
        sb.AppendLine("internal static class AllInOneModules");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Runs every service from platform.yaml in this process.</summary>");
        sb.AppendLine($"    public static global::Microsoft.AspNetCore.Builder.WebApplicationBuilder AddEveryModule(this global::Microsoft.AspNetCore.Builder.WebApplicationBuilder builder)");
        sb.AppendLine("    {");
        sb.AppendLine($"        builder.AddPlatform({string.Join(", ", platform.Services.Select(s => $"global::{product}.Core.Platform.PlatformServices.{s.Pascal}"))});");
        foreach (var service in platform.Services)
            sb.AppendLine($"        builder.AddModule<global::{product}.{service.Pascal}.{service.Pascal}Module>();");
        sb.AppendLine("        return builder;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        files.Add($"{Dir}/AllInOneModules.g.cs", "//", sb.ToString());
    }
}
