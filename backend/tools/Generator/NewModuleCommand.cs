using System.Globalization;
using System.Text;
using Acme.Core.Platform;

namespace Acme.Generator;

/// <summary>
/// <c>scripts/new-module &lt;key&gt;</c>: adds a module to <c>platform.yaml</c>, creates its backend (module, host, tests) and frontend
/// module from the clean templates in <c>templates/</c>, generates everything that follows from platform.yaml (constants, migrations
/// projects, gateway route, AppHost, compose, CI workflow, API client), creates its first migrations for every database provider,
/// and installs the frontend dependencies. No other file needs a manual edit.
/// </summary>
internal static class NewModuleCommand
{
    public static int Run(string repoRoot, string[] args)
    {
        var key = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? throw new PlatformDefinitionException("Usage: scripts/new-module <module-key> [--name \"Display Name\"] [--no-frontend]");
        var displayName = Option(args, "--name") ?? DisplayName(key);
        var withFrontend = !args.Contains("--no-frontend");

        var platform = PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml"));
        Check(platform, key);
        var pascal = Naming.Pascal(key);
        var port = Math.Max(5100, platform.Services.Max(s => s.Port)) + 1;
        var frontendPort = 3100 + platform.Modules.Count + 1;

        Console.WriteLine($"Adding module '{key}' ({pascal}), backend port {port}.");
        AddToPlatformYaml(Path.Combine(repoRoot, "platform.yaml"), key, displayName, port, platform.Plans);

        // 1. The backend, from the dotnet new template.
        Run("dotnet", repoRoot, "new", "install", Path.Combine(repoRoot, "templates", "backend-module"), "--force");
        Run("dotnet", repoRoot, "new", "acme-module", "--name", pascal, "--key", key, "--camel", Naming.Camel(key), "--product", platform.ProductPascal,
            "--output", Path.Combine(repoRoot, "backend"), "--force");

        // 2. The frontend module, from its template.
        if (withFrontend)
            CopyFrontend(repoRoot, key, pascal, displayName, frontendPort);

        // 3. Everything that follows from platform.yaml. First without the API files (they need the module to compile)...
        Console.WriteLine("Generating from platform.yaml...");
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: true, skipApi: true);

        // 4. The module's first migration, for every database provider.
        foreach (var provider in DataContexts.Providers(platform))
        {
            // `dotnet ef` is a local tool: it is only on the path inside backend/ (see backend/.config/dotnet-tools.json).
            var backend = Path.Combine(repoRoot, "backend");
            var project = $"src/Modules/{pascal}.Migrations.{provider.Suffix}";
            Run("dotnet", backend, "restore", project);
            Run("dotnet", backend, "ef", "migrations", "add", "Initial", "-p", project, "-s", project, "-c", $"{pascal}DbContext", "-o", $"Migrations/{pascal}");

            // The catalog lists every module and which plans include it (seed data from platform.yaml), so it changes too.
            var catalog = $"src/Core.Catalog.Migrations.{provider.Suffix}";
            Run("dotnet", backend, "restore", catalog);
            Run("dotnet", backend, "ef", "migrations", "add", $"Module{pascal}", "-p", catalog, "-s", catalog, "-c", "CatalogDbContext", "-o", "Migrations/Catalog");
        }

        // 5. ...then the API documents, typed clients and compose file, which build the module as it now is.
        GenerateCommand.Run(repoRoot, checkOnly: false, skipCompose: false, skipApi: false);

        if (withFrontend)
            Run(PnpmPath.Resolve(),Path.Combine(repoRoot, "frontend"), "install");

        Console.WriteLine();
        Console.WriteLine($"Done. '{key}' is ready: backend/src/Modules/{pascal}, backend/src/Hosts/{pascal}.Host, backend/tests/{pascal}.Tests" + (withFrontend ? $", frontend/modules/{key}." : "."));
        Console.WriteLine("Next: build and test (dotnet test --project backend/tests/" + pascal + ".Tests), then replace the example Item with your domain.");
        return 0;
    }

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
