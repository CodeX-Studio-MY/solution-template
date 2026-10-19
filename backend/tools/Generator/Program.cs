using System.Globalization;
using Saaskit.Generator;

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
        {
            // A business module declared in platform.yaml by hand has no code yet: scaffold it first, as scripts/new-module would.
            var unscaffolded = NewModuleCommand.Unscaffolded(repoRoot, PlatformDefinition.Load(Path.Combine(repoRoot, "platform.yaml")));
            if (args.Contains("--check") && unscaffolded.Count > 0)
            {
                Console.Error.WriteLine($"platform.yaml declares modules with no code yet: {string.Join(", ", unscaffolded)}. Run scripts/generate to scaffold them.");
                return 1;
            }
            foreach (var key in unscaffolded)
                NewModuleCommand.Run(repoRoot, [key, "--resume", .. args.Contains("--skip-compose") ? ["--skip-compose"] : Array.Empty<string>()]);
            return GenerateCommand.Run(repoRoot, args.Contains("--check"), args.Contains("--skip-compose"), args.Contains("--skip-api"));
        }
        case "new-module":
            return NewModuleCommand.Run(repoRoot, args[1..]);
        case "add-postgresql":
            return AddPostgreSqlCommand.Run(repoRoot, args.Contains("--skip-compose"), args.Contains("--skip-api"));
        case "init":
            return InitCommand.Run(repoRoot, args[1..]);
        case "rename" when args.Length == 5 && args[1] == "--from" && args[3] == "--to":
            var renamed = ProductRenamer.Run(repoRoot, args[2], args[4]);
            Console.WriteLine($"Renamed '{args[2]}' to '{args[4]}' in {renamed.ChangedFiles.Count} files and {renamed.RenamedPaths.Count} paths.");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Commands: generate [--check] [--skip-compose] [--skip-api] | init (see scripts/generate) | new-module <key> [--name \"Display Name\"] [--no-frontend] [--skip-compose] | add-postgresql [--skip-compose] [--skip-api] | rename --from <product> --to <product>");
            return 2;
    }
}
catch (PlatformDefinitionException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
