namespace Saaskit.Generator;

/// <summary>
/// The folders a module lives in (backend module, migrations, host, tests, frontend module), and the ones that belong to no service
/// or module in platform.yaml: what an abandoned run left behind, or what an editor wrote back from an open tab after the folder was
/// deleted. Such a folder is never built into the solution.
/// </summary>
internal static class ModuleFolders
{
    /// <summary>Folders of the platform that aren't named after a key.</summary>
    private static readonly string[] PlatformFolders = ["AllInOne.Host", "Architecture.Tests", "Core.Tests", "Generator.Tests", "Integration.Tests", "TestSupport"];

    /// <summary>The backend folders that hold one project each, named <c>{Pascal}</c> or <c>{Pascal}.{Kind}</c>.</summary>
    private static readonly string[] BackendFolders = ["backend/src/Modules", "backend/src/Hosts", "backend/tests"];

    /// <summary>Stray folders, as paths relative to the repository root with forward slashes.</summary>
    /// <param name="repoRoot">The repository root.</param>
    /// <param name="platform">platform.yaml.</param>
    /// <param name="adding">Module keys about to be added, which aren't strays even though platform.yaml doesn't list them yet.</param>
    public static IReadOnlyList<string> Strays(string repoRoot, PlatformDefinition platform, IEnumerable<string> adding)
    {
        var keys = platform.Services.Select(s => s.Key).Concat(platform.Modules.Select(m => m.Key)).Concat(adding).ToHashSet(StringComparer.Ordinal);
        var pascal = keys.Select(Naming.Pascal).ToHashSet(StringComparer.Ordinal);

        static IEnumerable<string> Folders(string path) =>
            Directory.Exists(path) ? Directory.EnumerateDirectories(path).Select(Path.GetFileName).OfType<string>() : [];

        var strays = new List<string>();
        foreach (var folder in BackendFolders)
        {
            strays.AddRange(Folders(Path.Combine(repoRoot, folder))
                .Where(name => !PlatformFolders.Contains(name) && !pascal.Contains(name.Split('.')[0]))
                .Select(name => $"{folder}/{name}"));
        }
        strays.AddRange(Folders(Path.Combine(repoRoot, "frontend", "modules"))
            .Where(name => !keys.Contains(name) && !name.StartsWith("lint-fixture-", StringComparison.Ordinal))
            .Select(name => $"frontend/modules/{name}"));
        return strays;
    }

    /// <summary>What to tell the person about stray folders.</summary>
    public static string Explain(IReadOnlyList<string> strays) =>
        "These folders belong to no module in platform.yaml:\n  " + string.Join("\n  ", strays) +
        "\nThey are left over from an earlier run, or an editor wrote them back from a tab that was still open. Close those tabs, " +
        "delete the folders (they are not tracked by git), then run scripts/generate again.";
}
