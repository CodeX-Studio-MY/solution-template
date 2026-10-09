using System.Reflection;
using Acme.Core.Platform;

namespace Acme.Architecture.Tests;

/// <summary>The assemblies and source files the rules inspect.</summary>
internal static class Solution
{
    public static readonly string Product = typeof(PlatformInfo).Assembly.GetName().Name!.Split('.')[0];

    public static string RepoRoot { get; } = FindRepoRoot();

    public static string BackendSource => Path.Combine(RepoRoot, "backend", "src");

    /// <summary>Every production assembly in the output folder (ours, by product prefix), excluding tests and tools.</summary>
    public static IReadOnlyList<Assembly> Assemblies { get; } = Directory
        .EnumerateFiles(AppContext.BaseDirectory, $"{Product}.*.dll")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(name => !name!.EndsWith(".Tests", StringComparison.Ordinal) && name != $"{Product}.Generator")
        .Select(name => Assembly.Load(name!))
        .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>Business and platform module assemblies: one per service in platform.yaml.</summary>
    public static IReadOnlyList<Assembly> ModuleAssemblies { get; } = PlatformServices.All
        .Select(s => Assemblies.Single(a => a.GetName().Name == $"{Product}.{Pascal(s.Key)}"))
        .ToList();

    public static bool IsMigrations(Assembly assembly) => assembly.GetName().Name!.Contains(".Migrations.", StringComparison.Ordinal);

    public static string Pascal(string key) =>
        string.Concat(key.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    /// <summary>Hand-written C# under backend/src (generated code and EF migrations are excluded).</summary>
    public static IEnumerable<string> HandWrittenSources() =>
        Directory.EnumerateFiles(BackendSource, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(BackendSource, path).Replace('\\', '/');
                return !relative.Contains("/bin/", StringComparison.Ordinal)
                    && !relative.Contains("/obj/", StringComparison.Ordinal)
                    && !relative.Contains("/Generated/", StringComparison.Ordinal)
                    && !relative.Contains("/Migrations/", StringComparison.Ordinal)
                    && !relative.EndsWith(".g.cs", StringComparison.Ordinal);
            });

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "platform.yaml")))
                return dir.FullName;
        }
        throw new InvalidOperationException("platform.yaml not found above the test output folder.");
    }
}
