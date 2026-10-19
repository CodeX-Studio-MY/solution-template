using System.Text;

namespace Saaskit.Generator.Emitters;

/// <summary>backend/{product}.slnx: every project under backend/, so a new module never needs a manual solution edit.</summary>
internal static class SolutionEmitter
{
    public static void Emit(PlatformDefinition platform, GeneratedFiles files)
    {
        var backend = Path.Combine(files.RepoRoot, "backend");
        var onDisk = Directory.EnumerateFiles(backend, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(backend, p).Replace('\\', '/'));
        var generated = files.Files.Keys
            .Where(p => p.StartsWith("backend/", StringComparison.Ordinal) && p.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(p => p["backend/".Length..]);
        var projects = onDisk.Concat(generated)
            .Where(p => !p.StartsWith("templates/", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<Solution>");
        foreach (var folder in projects.GroupBy(FolderOf).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"  <Folder Name=\"/{folder.Key}/\">");
            foreach (var project in folder)
                sb.AppendLine($"    <Project Path=\"{project}\" />");
            sb.AppendLine("  </Folder>");
        }
        sb.AppendLine("</Solution>");
        files.AddXml($"backend/{platform.Product}.slnx", sb.ToString());
    }

    /// <summary><c>src/Modules/X/X.csproj</c> → <c>src/Modules</c>; <c>src/Core/Core.csproj</c> → <c>src</c>.</summary>
    private static string FolderOf(string project)
    {
        var parts = project.Split('/');
        return parts.Length > 3 ? $"{parts[0]}/{parts[1]}" : parts[0];
    }
}
