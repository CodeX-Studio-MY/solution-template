using System.Globalization;
using Acme.Generator;

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
            return GenerateCommand.Run(repoRoot, args.Contains("--check"), args.Contains("--skip-compose"), args.Contains("--skip-api"));
        case "new-module":
            return NewModuleCommand.Run(repoRoot, args[1..]);
        default:
            Console.Error.WriteLine($"Unknown command '{command}'. Commands: generate [--check] [--skip-compose] [--skip-api] | new-module <key> [--name \"Display Name\"] [--no-frontend]");
            return 2;
    }
}
catch (PlatformDefinitionException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
