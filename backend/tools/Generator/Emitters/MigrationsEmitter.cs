using System.Text;

namespace Saaskit.Generator.Emitters;

/// <summary>
/// One migrations project per context project and provider, with design-time factories, plus the Migrator's
/// target registry and project references. Migration files themselves are written by <c>dotnet ef migrations add</c>.
/// </summary>
internal static class MigrationsEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var product = platform.ProductPascal;
        var contexts = DataContexts.From(platform);
        var providers = DataContexts.Providers(platform);
        var migrationsProjects = new List<string>();

        foreach (var group in contexts.GroupBy(c => c.Project))
        {
            foreach (var provider in providers)
            {
                var project = group.First().MigrationsProject(provider.Suffix);
                var name = Path.GetFileName(project);
                var dir = $"backend/src/{Path.GetDirectoryName(group.Key)!.Replace('\\', '/')}/../{name}";
                dir = Normalise(dir);
                migrationsProjects.Add($"{dir}/{name}.csproj");

                var contextProject = Path.GetFileName(group.Key);
                files.AddXml($"{dir}/{name}.csproj", $"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <!-- {provider.Suffix} migrations for {contextProject}. Add one with: dotnet ef migrations add <Name> -p {dir["backend/".Length..]} -c <Name>DbContext -o Migrations/<Name> -->
                      <PropertyGroup>
                        <IsPackable>false</IsPackable>
                      </PropertyGroup>
                      <ItemGroup>
                        <ProjectReference Include="{Relative(dir, $"backend/src/{group.Key}.csproj")}" />
                        <ProjectReference Include="{Relative(dir, $"backend/src/Core.Data.{provider.Suffix}/Core.Data.{provider.Suffix}.csproj")}" />
                      </ItemGroup>
                      <ItemGroup>
                        <PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
                      </ItemGroup>
                    </Project>
                    """);

                var sb = new StringBuilder();
                sb.AppendLine("#nullable enable");
                sb.AppendLine("using Microsoft.EntityFrameworkCore.Design;");
                sb.AppendLine();
                sb.AppendLine($"namespace {product}.{name};");
                foreach (var context in group)
                {
                    sb.AppendLine();
                    sb.AppendLine($"/// <summary>Lets <c>dotnet ef</c> build {context.ContextName} for {provider.Suffix}.</summary>");
                    sb.AppendLine($"internal sealed class {context.ContextName}DesignTimeFactory : IDesignTimeDbContextFactory<global::{context.ContextType}>");
                    sb.AppendLine("{");
                    sb.AppendLine($"    public global::{context.ContextType} CreateDbContext(string[] args) =>");
                    sb.AppendLine($"        global::{product}.Core.Data.DbContextRegistration.Create<global::{context.ContextType}>(");
                    sb.AppendLine("            null,");
                    sb.AppendLine($"            new global::{product}.{provider.ProviderType}(),");
                    sb.AppendLine($"            {Naming.Quote(provider.DesignTimeConnectionString)},");
                    sb.AppendLine($"            global::{context.SchemaExpression});");
                    sb.AppendLine("}");
                }
                files.Add($"{dir}/DesignTimeFactories.g.cs", "//", sb.ToString());
            }
        }

        EmitMigrator(platform, files, contexts, migrationsProjects);
    }

    private static void EmitMigrator(PlatformDefinition platform, GeneratedFiles files, IReadOnlyList<DataContext> contexts, List<string> migrationsProjects)
    {
        const string migratorDir = "backend/src/Migrator";
        var references = string.Join("\n", migrationsProjects.Select(p => $"    <ProjectReference Include=\"$(MSBuildThisFileDirectory){Relative($"{migratorDir}/Generated", p)}\" />"));
        files.AddXml($"{migratorDir}/Generated/MigrationsProjects.g.props", $"""
            <Project>
              <ItemGroup>
            {references}
              </ItemGroup>
            </Project>
            """);

        var product = platform.ProductPascal;
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine($"namespace {product}.Migrator;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class MigrationTargets");
        sb.AppendLine("{");
        foreach (var database in new[] { "catalog", "tenant" })
        {
            sb.AppendLine($"    /// <summary>Contexts in the {database} database, in the order they are migrated.</summary>");
            sb.AppendLine($"    public static readonly IReadOnlyList<MigrationTarget> {Naming.Pascal(database)} =");
            sb.AppendLine("    [");
            foreach (var context in contexts.Where(c => c.Database == database))
            {
                sb.AppendLine($"        new({Naming.Quote(context.Target)}, (provider, connectionString) =>");
                sb.AppendLine($"            global::{product}.Core.Data.DbContextRegistration.Create<global::{context.ContextType}>(null, provider, connectionString, global::{context.SchemaExpression})),");
            }
            sb.AppendLine("    ];");
            sb.AppendLine();
        }
        sb.Length -= Environment.NewLine.Length;
        sb.AppendLine("}");
        files.Add($"{migratorDir}/Generated/MigrationTargets.g.cs", "//", sb.ToString());
    }

    internal static string Relative(string fromDir, string toPath) =>
        Path.GetRelativePath("/r/" + fromDir, "/r/" + toPath).Replace('/', '\\');

    private static string Normalise(string path) =>
        Path.GetRelativePath("/r", Path.GetFullPath("/r/" + path)).Replace('\\', '/');
}
