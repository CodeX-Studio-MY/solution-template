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

    /// <summary>
    /// Deletes stray folders and says so. A folder holding a file git tracks is someone's committed work, so it is kept and the run
    /// stops; the rest (untracked, or no git repository) are build leftovers and go.
    /// </summary>
    public static void Delete(string repoRoot, IReadOnlyList<string> strays)
    {
        var tracked = strays.Where(stray => IsTracked(repoRoot, stray)).ToList();
        if (tracked.Count > 0)
            throw new PlatformDefinitionException(
                "These folders belong to no module in platform.yaml but hold files committed to git, so they were not deleted:\n  " +
                string.Join("\n  ", tracked) + "\nAdd their module to platform.yaml, or delete them yourself, then run scripts/generate again.");

        foreach (var stray in strays)
        {
            var path = Path.Combine(repoRoot, stray);
            for (var attempt = 1; Directory.Exists(path); attempt++)
            {
                try
                {
                    Directory.Delete(path, recursive: true);
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
                {
                    Thread.Sleep(400);   // an editor or build server may still hold a file in it
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new PlatformDefinitionException($"{stray} belongs to no module in platform.yaml and couldn't be deleted ({ex.Message.TrimEnd('.')}). " +
                        "Close the editor tabs or programs using it, then run scripts/generate again.");
                }
            }
            Console.WriteLine($"Deleted {stray}: it belongs to no module in platform.yaml (left over from an earlier run, or written back by an editor).");
        }
    }

    private static bool IsTracked(string repoRoot, string stray)
    {
        if (!Directory.Exists(Path.Combine(repoRoot, ".git")))
            return false;
        try
        {
            return Processes.Capture("git", repoRoot, "", "ls-files", "--", stray).Trim().Length > 0;
        }
        catch (PlatformDefinitionException)
        {
            return true;   // can't tell: keep it
        }
    }
}
