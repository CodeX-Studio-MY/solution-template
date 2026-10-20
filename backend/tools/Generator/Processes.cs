using System.Diagnostics;

namespace Farm360.Generator;

/// <summary>Runs the tools the generator commands depend on (dotnet, pnpm, git), with their output on this console.</summary>
internal static class Processes
{
    /// <summary>Runs a command and returns its exit code; the command is echoed first.</summary>
    public static int Exec(string fileName, string workingDirectory, params string[] arguments)
    {
        Console.WriteLine($"> {fileName} {string.Join(' ', arguments)}");
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PlatformDefinitionException($"'{fileName}' is required. Install it and run this again.");
        }
        using (process)
        {
            process.WaitForExit();
            return process.ExitCode;
        }
    }

    /// <summary>Runs a command and returns what it wrote to standard output; fails with <paramref name="failureHint"/> when it exits non-zero.</summary>
    public static string Capture(string fileName, string workingDirectory, string failureHint, params string[] arguments)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new PlatformDefinitionException($"'{fileName}' is required. Install it and run this again.");
        }
        using (process)
        {
            // Read both streams before waiting, so a full pipe can't block the child.
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new PlatformDefinitionException($"'{fileName} {string.Join(' ', arguments)}' failed ({process.ExitCode}). {failureHint}\n{error.Result}");
            return output;
        }
    }

    /// <summary>Runs a command and fails with <paramref name="failureHint"/> when it exits non-zero.</summary>
    public static void Run(string fileName, string workingDirectory, string failureHint, params string[] arguments)
    {
        var exitCode = Exec(fileName, workingDirectory, arguments);
        if (exitCode != 0)
            throw new PlatformDefinitionException($"'{fileName} {string.Join(' ', arguments)}' failed ({exitCode}). {failureHint}");
    }
}
