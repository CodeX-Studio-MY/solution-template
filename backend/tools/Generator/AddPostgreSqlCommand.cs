using System.Text;
using System.Text.RegularExpressions;

namespace Farm360.Generator;

/// <summary>
/// <c>scripts/add-postgresql</c>: puts PostgreSQL back into <c>platform.yaml</c>, restores the PostgreSQL migrations projects from the
/// commit that last had them, catches them up to the current model, warns about hand-written SQL in later SQL Server migrations, and runs
/// the migrations consistency tests. The Scheduler's migration holds hand-written Quartz SQL, so it is restored, never regenerated.
/// </summary>
internal static partial class AddPostgreSqlCommand
{
    /// <summary>The last commit with a migrations project for PostgreSQL (17db99a, "M6: delivery and docs").</summary>
    private const string BaselineCommit = "17db99a6f991590281d7dd5443584b7ed24fb157";
    private const string Hint = "platform.yaml was already changed; fix the problem and run scripts/add-postgresql again, or revert with git.";

    public static int Run(string repoRoot, bool skipCompose, bool skipApi)
    {
        var yamlPath = Path.Combine(repoRoot, "platform.yaml");
        var platform = PlatformDefinition.Load(yamlPath);
        if (platform.Database.Providers.Contains("postgresql"))
            throw new PlatformDefinitionException("platform.yaml already lists postgresql; nothing to add.");

        // 1. Preconditions.
        if (Processes.Exec("git", repoRoot, "cat-file", "-e", $"{BaselineCommit}^{{commit}}") != 0)
            throw new PlatformDefinitionException($"Commit {BaselineCommit[..7]} (the last with PostgreSQL migrations) is not in this clone. If it is shallow, run: git fetch --unshallow");
        Console.WriteLine("Note: uncommitted changes are mixed with this command's changes; commit or stash first if you want a clean diff.");

        // 2. platform.yaml.
        SetProviders(yamlPath);
        platform = PlatformDefinition.Load(yamlPath);

        // 3. The migrations projects of the baseline: the Scheduler's hand-written Quartz SQL comes back exactly as it was.
        Processes.Run("git", repoRoot, Hint, "restore", $"--source={BaselineCommit}", "--", ":(glob)backend/src/**/*.Migrations.PostgreSql/**");

        // 4. Projects (slnx, Migrator references, constants) for what was added since, without the API files (they need the build).
        Console.WriteLine("Generating from platform.yaml...");
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: true, skipApi: true);

        // 5. A migration per context where the model moved on, and a first one for contexts that did not exist at the baseline.
        var backend = Path.Combine(repoRoot, "backend");
        var postgres = Provider.For("postgresql");
        var warnings = new List<string>();
        // `dotnet ef` is a local tool (backend/.config/dotnet-tools.json); a fresh clone has not restored it yet.
        Processes.Run("dotnet", backend, Hint, "tool", "restore");
        foreach (var group in DataContexts.From(platform).GroupBy(c => c.Project))
        {
            var relative = $"src/{ProjectDirectory(group.Key, postgres.Suffix)}";
            var restoredNewest = NewestMigration(Path.Combine(backend, relative));
            Processes.Run("dotnet", backend, Hint, "restore", relative);

            foreach (var context in group)
            {
                var snapshot = Directory.Exists(Path.Combine(backend, relative))
                    ? Directory.EnumerateFiles(Path.Combine(backend, relative), $"{context.ContextName}ModelSnapshot.cs", SearchOption.AllDirectories).FirstOrDefault()
                    : null;
                if (snapshot is null)
                {
                    var folder = $"Migrations/{context.ContextName.Replace("DbContext", "", StringComparison.Ordinal)}";
                    Processes.Run("dotnet", backend, Hint, "ef", "migrations", "add", "Initial", "-p", relative, "-s", relative, "-c", context.ContextName, "-o", folder);
                    continue;
                }

                // Exit code 0: the model matches the snapshot; non-zero: changes (or an error, which the add below then reports).
                if (Processes.Exec("dotnet", backend, "ef", "migrations", "has-pending-model-changes", "-p", relative, "-s", relative, "-c", context.ContextName) != 0)
                {
                    var folder = Path.GetRelativePath(Path.Combine(backend, relative), Path.GetDirectoryName(snapshot)!).Replace('\\', '/');
                    Processes.Run("dotnet", backend, Hint, "ef", "migrations", "add", "PostgreSqlCatchUp", "-p", relative, "-s", relative, "-c", context.ContextName, "-o", folder);
                    warnings.Add($"{context.ContextName}: added a PostgreSqlCatchUp migration in {relative}/{folder}; review it.");
                }
            }

            // 6. Hand-written SQL in SQL Server migrations newer than the newest restored PostgreSQL migration is not in the catch-up.
            warnings.AddRange(HandWrittenSql(backend, ProjectDirectory(group.Key, "SqlServer"), restoredNewest));
        }

        // 7. The API files, typed clients and compose file, as the backend now is.
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: skipCompose, skipApi: skipApi);

        // 8. The model and the migrations of every context agree, on every provider (no database needed).
        var exit = Processes.Exec("dotnet", repoRoot, "test", "--project", "backend/tests/Integration.Tests", "--filter-class", "Farm360.Integration.Tests.MigrationsConsistencyTests");

        // 9. What is left for a person.
        Console.WriteLine();
        foreach (var warning in warnings)
            Console.WriteLine($"WARNING: {warning}");
        Console.WriteLine(exit == 0
            ? "PostgreSQL is back: migrations restored and consistent with the model."
            : "The consistency tests failed: a PostgreSQL migration does not match the model (see above).");
        Console.WriteLine("Next: review the changes, run the full suite with Docker (it now starts PostgreSQL too), then commit.");
        return exit;
    }

    /// <summary>"Modules/Identity/Identity" becomes "Modules/Identity.Migrations.{suffix}"; "Core.Catalog/Core.Catalog" becomes "Core.Catalog.Migrations.{suffix}".</summary>
    private static string ProjectDirectory(string contextProject, string suffix)
    {
        var name = Path.GetFileName(contextProject);
        var parent = Path.GetDirectoryName(contextProject)!.Replace('\\', '/');
        var folder = parent.Contains('/') ? parent[..parent.LastIndexOf('/')] : "";
        return string.IsNullOrEmpty(folder) ? $"{name}.Migrations.{suffix}" : $"{folder}/{name}.Migrations.{suffix}";
    }

    private static string NewestMigration(string projectDirectory) =>
        !Directory.Exists(projectDirectory)
            ? ""
            : Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                .Select(f => MigrationFile().Match(Path.GetFileName(f)))
                .Where(m => m.Success)
                .Select(m => m.Groups[1].Value)
                .DefaultIfEmpty("")
                .Max()!;

    private static IEnumerable<string> HandWrittenSql(string backend, string sqlServerProject, string newerThan)
    {
        var directory = Path.Combine(backend, "src", sqlServerProject);
        if (!Directory.Exists(directory))
            yield break;
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            var match = MigrationFile().Match(Path.GetFileName(file));
            if (!match.Success || string.CompareOrdinal(match.Groups[1].Value, newerThan) <= 0)
                continue;
            if (File.ReadAllText(file).Contains("migrationBuilder.Sql(", StringComparison.Ordinal))
                yield return $"{Path.GetRelativePath(backend, file).Replace('\\', '/')} has hand-written SQL newer than the PostgreSQL migrations: port it by hand into the PostgreSQL catch-up migration.";
        }
    }

    private static void SetProviders(string path)
    {
        var text = File.ReadAllText(path);
        var updated = ProvidersLine().Replace(text, m => $"{m.Groups[1].Value}[{m.Groups[2].Value.Trim()}, postgresql]", 1);
        if (updated == text)
            throw new PlatformDefinitionException("platform.yaml has no 'providers: [...]' line to extend.");
        File.WriteAllText(path, updated, new UTF8Encoding(false));
    }

    /// <summary>A migration's file: <c>{14-digit timestamp}_{Name}.cs</c>, not its Designer file nor a snapshot.</summary>
    [GeneratedRegex(@"^(\d{14})_[^.]+\.cs$")]
    private static partial Regex MigrationFile();

    [GeneratedRegex(@"^(\s*providers:\s*)\[([^\]]*)\]", RegexOptions.Multiline)]
    private static partial Regex ProvidersLine();
}
