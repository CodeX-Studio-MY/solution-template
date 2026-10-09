using System.Globalization;
using Acme.Generator;
using Acme.Generator.Emitters;

// Generated output must be identical on every machine.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var repoRoot = RepoRoot.Find();
var command = args.FirstOrDefault() ?? "generate";

try
{
    switch (command)
    {
        case "generate":
            return Generate(args.Contains("--check"), args.Contains("--skip-compose"), args.Contains("--skip-api"));
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Commands: generate [--check] [--skip-compose] [--skip-api]");
            return 2;
    }
}
catch (PlatformDefinitionException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

int Generate(bool checkOnly, bool skipCompose, bool skipApi)
{
    var platform = PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml"));

    // Phase 1: everything generated from platform.yaml and the configuration schema.
    var files = new GeneratedFiles(repoRoot);
    MsBuildEmitter.Emit(platform, files);
    CSharpPlatformEmitter.Emit(platform, files);
    MigrationsEmitter.Emit(platform, files);
    AppHostEmitter.Emit(platform, files);
    AllInOneEmitter.Emit(platform, files);
    TypeScriptPlatformEmitter.Emit(platform, files);
    ConfigurationEmitter.Emit(platform, files);
    ReadmeEmitter.Emit(platform, files);
    // Last: it lists the projects generated above.
    SolutionEmitter.Emit(platform, files);
    var changed = Report(files.Write(checkOnly));
    var total = files.Files.Count;

    // Phase 2: the OpenAPI documents, error codes, events and typed API clients, built from the backend as it now is.
    if (skipApi)
    {
        Console.WriteLine("Skipped the API files (--skip-api).");
    }
    else if (checkOnly && changed.Count > 0)
    {
        Console.WriteLine("Skipped the API check: the inputs above are out of date.");
    }
    else
    {
        var api = Report(ApiEmitter.Run(platform, repoRoot, checkOnly));
        changed = [.. changed, .. api];
        total += 1;
    }

    // Phase 3: docker compose, published from the AppHost as it now is on disk.
    if (skipCompose)
    {
        Console.WriteLine("Skipped deploy/docker-compose.yml (--skip-compose).");
    }
    else if (checkOnly && changed.Count > 0)
    {
        Console.WriteLine("Skipped the compose check: the AppHost inputs above are out of date.");
    }
    else
    {
        var compose = new GeneratedFiles(repoRoot);
        ComposeEmitter.Emit(compose);
        changed = [.. changed, .. Report(compose.Write(checkOnly))];
        total += compose.Files.Count;
    }

    Console.WriteLine($"{total} generated files, {changed.Count} {(checkOnly ? "out of date" : "changed")}.");
    return checkOnly && changed.Count > 0 ? 1 : 0;

    IReadOnlyList<string> Report(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
            Console.WriteLine($"{(checkOnly ? "out of date" : "updated")}: {path}");
        return paths;
    }
}
