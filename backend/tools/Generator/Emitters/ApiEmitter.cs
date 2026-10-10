using System.Diagnostics;

namespace Acme.Generator.Emitters;

/// <summary>
/// Everything the frontend is generated from the backend: each service's OpenAPI document, the error-code union and the event
/// catalog (by <c>tools/ApiExporter</c>, which builds the service hosts in memory), then the typed API clients (by
/// <c>openapi-typescript</c> in <c>frontend/packages/api-clients</c>). It runs after the platform.yaml outputs because it compiles them.
/// </summary>
internal static class ApiEmitter
{
    private static readonly string[] CheckFlag = ["--check"];
    private static readonly string[] CheckFlagForScript = ["--", "--check"];

    /// <summary>Returns the files that are (or, with <paramref name="checkOnly"/>, would be) changed.</summary>
    public static IReadOnlyList<string> Run(PlatformDefinition platform, string repoRoot, bool checkOnly)
    {
        var changed = new List<string>();
        var exporter = Run("dotnet", repoRoot, ["run", "--project", "backend/tools/ApiExporter", "--", repoRoot, .. checkOnly ? CheckFlag : []], allowExitCode1: true);
        changed.AddRange(ChangedPaths(exporter.Output));
        if (exporter.ExitCode > 1 || (exporter.ExitCode == 1 && changed.Count == 0))
            throw new PlatformDefinitionException($"The API exporter failed:\n{exporter.Output}");

        var pnpm = PnpmPath.Resolve();
        var clients = Run(pnpm, repoRoot, ["--dir", "frontend", "--filter", $"@{platform.Product}/api-clients", "run", "generate", .. checkOnly ? CheckFlagForScript : []], allowExitCode1: true);
        changed.AddRange(ChangedPaths(clients.Output));
        if (clients.ExitCode > 1 || (clients.ExitCode == 1 && ChangedPaths(clients.Output).Count == 0))
            throw new PlatformDefinitionException($"Generating the API clients failed:\n{clients.Output}");
        return changed;
    }

    /// <summary>The "out of date: path" and "updated: path" lines a step prints.</summary>
    private static List<string> ChangedPaths(string output) =>
        [.. output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("out of date: ", StringComparison.Ordinal) || line.StartsWith("updated: ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(": ", StringComparison.Ordinal) + 2)..])];

    private static (int ExitCode, string Output) Run(string fileName, string workingDirectory, string[] arguments, bool allowExitCode1)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["NO_COLOR"] = "1";
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PlatformDefinitionException($"'{fileName}' is required to generate the API files. Install it (pnpm: corepack enable) or pass --skip-api.");
        }
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            _ = allowExitCode1;
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
    }
}
