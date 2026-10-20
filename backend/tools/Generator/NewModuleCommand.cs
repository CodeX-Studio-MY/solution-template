using System.Globalization;
using System.Text;
using Farm360.Core.Platform;

namespace Farm360.Generator;

/// <summary>
/// <c>scripts/new-module &lt;key&gt;</c>: adds a module to <c>platform.yaml</c>, creates its backend (module, host, tests) and frontend
/// module from the clean templates in <c>templates/</c>, generates everything that follows from platform.yaml (constants, migrations
/// projects, gateway route, AppHost, compose, API client), creates its first migrations for every database provider,
/// and installs the frontend dependencies. No other file needs a manual edit.
/// </summary>
internal static class NewModuleCommand
{
    public static int Run(string repoRoot, string[] args)
    {
        var key = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? throw new PlatformDefinitionException("Usage: scripts/new-module <module-key> [--name \"Display Name\"] [--no-frontend] [--skip-compose] [--resume] [--no-catalog-migration]");
        var displayName = Option(args, "--name") ?? DisplayName(key);
        var withFrontend = !args.Contains("--no-frontend");
        var skipCompose = args.Contains("--skip-compose");
        // --resume: the module is already in platform.yaml (an earlier run stopped part-way, or someone declared it there by hand);
        // create whatever of it is missing and finish the remaining steps.
        var resume = args.Contains("--resume");
        // --no-catalog-migration: the caller makes the catalog's migration itself (scripts/generate on the template recreates it once).
        var catalogMigration = !args.Contains("--no-catalog-migration");

        var platform = PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml"));
        var pascal = Naming.Pascal(key);
        if (resume)
        {
            var declared = platform.Modules.FirstOrDefault(m => m.Key == key)
                ?? throw new PlatformDefinitionException($"'{key}' is not in platform.yaml, so there is nothing to resume.");
            var hasBackend = Directory.Exists(BackendFolder(repoRoot, pascal));
            Console.WriteLine(hasBackend ? $"Resuming module '{key}' ({pascal})." : $"Scaffolding module '{key}' ({pascal}), declared in platform.yaml.");

            // 1 and 2, for whatever of the module isn't on disk yet. Its name and ports are the ones platform.yaml declares.
            if (!hasBackend)
            {
                KeyCollisions.Check(repoRoot, key);
                CreateBackend(repoRoot, platform, key, pascal);
            }
            if (withFrontend && !Directory.Exists(Path.Combine(repoRoot, "frontend", "modules", key)))
                CopyFrontend(repoRoot, key, pascal, declared.Name, 3100 + platform.Modules.IndexOf(declared) + 1);
        }
        else
        {
            Check(platform, key);
            KeyCollisions.Check(repoRoot, key);
            var port = Math.Max(5100, platform.Services.Max(s => s.Port)) + 1;
            var frontendPort = 3100 + platform.Modules.Count + 1;

            Console.WriteLine($"Adding module '{key}' ({pascal}), backend port {port}.");
            AddToPlatformYaml(Path.Combine(repoRoot, "platform.yaml"), key, displayName, port, platform.Plans);

            // 1. The backend, from the dotnet new template.
            CreateBackend(repoRoot, platform, key, pascal);

            // 2. The frontend module, from its template.
            if (withFrontend)
                CopyFrontend(repoRoot, key, pascal, displayName, frontendPort);
        }

        // 3. Everything that follows from platform.yaml. First without the API files (they need the module to compile)...
        Console.WriteLine("Generating from platform.yaml...");
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: true, skipApi: true);

        // 4. The module's first migration, for every database provider. Each is skipped if an earlier run already created it.
        foreach (var provider in DataContexts.Providers(platform))
        {
            // `dotnet ef` is a local tool: it is only on the path inside backend/ (see backend/.config/dotnet-tools.json).
            var backend = Path.Combine(repoRoot, "backend");
            var project = $"src/Modules/{pascal}.Migrations.{provider.Suffix}";
            if (!HasMigration(Path.Combine(backend, project, "Migrations", pascal), "Initial"))
            {
                Run("dotnet", backend, "restore", project);
                Run("dotnet", backend, "ef", "migrations", "add", "Initial", "-p", project, "-s", project, "-c", $"{pascal}DbContext", "-o", $"Migrations/{pascal}");
            }

            // The catalog lists every module and which plans include it (seed data from platform.yaml), so it changes too.
            var catalog = $"src/Core.Catalog.Migrations.{provider.Suffix}";
            if (catalogMigration && !HasMigration(Path.Combine(backend, catalog, "Migrations", "Catalog"), $"Module{pascal}"))
            {
                Run("dotnet", backend, "restore", catalog);
                Run("dotnet", backend, "ef", "migrations", "add", $"Module{pascal}", "-p", catalog, "-s", catalog, "-c", "CatalogDbContext", "-o", "Migrations/Catalog");
            }
        }

        // 5. ...then the API documents, typed clients and compose file, which build the module as it now is.
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: skipCompose, skipApi: false);

        if (withFrontend)
            Run(PnpmPath.Resolve(),Path.Combine(repoRoot, "frontend"), "install");

        Console.WriteLine();
        Console.WriteLine($"Done. '{key}' is ready: backend/src/Modules/{pascal}, backend/src/Hosts/{pascal}.Host, backend/tests/{pascal}.Tests" + (withFrontend ? $", frontend/modules/{key}." : "."));
        Console.WriteLine("Next: build and test (dotnet test --project backend/tests/" + pascal + ".Tests), then replace the example Item with your domain.");
        return 0;
    }

    /// <summary>The module's backend class library folder: <c>backend/src/Modules/{Pascal}</c>.</summary>
    internal static string BackendFolder(string repoRoot, string pascal) => Path.Combine(repoRoot, "backend", "src", "Modules", pascal);

    /// <summary>The backend (module, host, tests), from the dotnet new template.</summary>
    private static void CreateBackend(string repoRoot, PlatformDefinition platform, string key, string pascal)
    {
        Run("dotnet", repoRoot, "new", "install", Path.Combine(repoRoot, "templates", "backend-module"), "--force");
        Run("dotnet", repoRoot, "new", $"{platform.Product}-module", "--name", pascal, "--key", key, "--camel", Naming.Camel(key), "--product", platform.ProductPascal,
            "--output", Path.Combine(repoRoot, "backend"), "--force");
    }

    /// <summary>
    /// Business modules declared in platform.yaml (a service that owns a module of its own key) whose code isn't on disk yet: someone
    /// added them to platform.yaml by hand. scripts/generate scaffolds them.
    /// </summary>
    internal static IReadOnlyList<string> Unscaffolded(string repoRoot, PlatformDefinition platform) =>
        [.. platform.Services
            .Where(s => s.Owns.Contains(s.Key) && platform.Modules.Any(m => m.Key == s.Key))
            .Select(s => s.Key)
            .Where(key => !Directory.Exists(BackendFolder(repoRoot, Naming.Pascal(key))))];

    /// <summary>Whether <paramref name="directory"/> has a migration named <paramref name="name"/> (<c>{timestamp}_{name}.cs</c>).</summary>
    internal static bool HasMigration(string directory, string name) =>
        Directory.Exists(directory) && Directory.EnumerateFiles(directory, $"*_{name}.cs").Any();

    private static void Check(PlatformDefinition platform, string key)
    {
        if (!KeyRules.Pattern.IsMatch(key))
            throw new PlatformDefinitionException($"'{key}' is not a valid module key: it must match {KeyRules.Pattern}.");
        if (KeyRules.Reserved.Contains(key))
            throw new PlatformDefinitionException($"'{key}' is a reserved name.");
        if (platform.Modules.Any(m => m.Key == key) || platform.Services.Any(s => s.Key == key))
            throw new PlatformDefinitionException($"'{key}' already exists in platform.yaml.");
    }

    /// <summary>Appends one line to the services list and one to the modules list, keeping the file's comments and layout.</summary>
    internal static void AddToPlatformYaml(string path, string key, string displayName, int port, IReadOnlyList<string> plans)
    {
        var lines = File.ReadAllLines(path).ToList();
        var servicesAt = lines.FindIndex(l => l.StartsWith("services:", StringComparison.Ordinal));
        var modulesAt = lines.FindIndex(l => l.StartsWith("modules:", StringComparison.Ordinal));
        if (servicesAt < 0 || modulesAt < 0)
            throw new PlatformDefinitionException("platform.yaml has no services: or modules: list.");

        lines.Insert(LastEntry(lines, modulesAt) + 1, $"  - {{ key: {key}, name: {displayName}, plans: [{string.Join(", ", plans)}] }}");
        lines.Insert(LastEntry(lines, servicesAt) + 1, $"  - {{ key: {key}, port: {port}, owns: [{key}] }}");
        File.WriteAllText(path, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    }

    /// <summary>The index of the last "  - { key: ..." line of the list that starts at <paramref name="header"/>.</summary>
    private static int LastEntry(List<string> lines, int header)
    {
        var last = -1;
        for (var i = header + 1; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("  - {", StringComparison.Ordinal))
                last = i;
            else if (lines[i].Length > 0 && !lines[i].StartsWith(' ') && !lines[i].StartsWith('#'))
                break;
        }
        return last >= 0 ? last : header;
    }

    private static void CopyFrontend(string repoRoot, string key, string pascal, string displayName, int port)
    {
        var source = Path.Combine(repoRoot, "templates", "frontend-module");
        var target = Path.Combine(repoRoot, "frontend", "modules", key);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, File.ReadAllText(file)
                .Replace("__KEY__", key, StringComparison.Ordinal)
                .Replace("__PASCAL__", pascal, StringComparison.Ordinal)
                .Replace("__DISPLAY__", displayName, StringComparison.Ordinal)
                .Replace("__PORT__", port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), new UTF8Encoding(false));
        }
    }

    private static string DisplayName(string key) =>
        string.Join(' ', key.Split('-').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void Run(string fileName, string workingDirectory, params string[] arguments) =>
        Processes.Run(fileName, workingDirectory, "platform.yaml was already changed; fix the problem and run scripts/generate, or revert the change.", arguments);
}
