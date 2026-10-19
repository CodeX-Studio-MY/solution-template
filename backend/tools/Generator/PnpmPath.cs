namespace Saaskit.Generator;

/// <summary>
/// Where pnpm is: the <c>PNPM</c> environment variable, the standalone installer's <c>~/.local/bin/pnpm</c>, or the PATH.
/// On Windows pnpm is a <c>pnpm.cmd</c> shim, which <see cref="System.Diagnostics.Process"/> neither finds by its bare name nor
/// runs correctly unless given its full path (the shim locates its script from its own directory), so it is resolved here.
/// </summary>
internal static class PnpmPath
{
    public static string Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("PNPM");
        if (!string.IsNullOrEmpty(fromEnvironment))
            return fromEnvironment;
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "pnpm");
        if (File.Exists(local))
            return local;
        if (!OperatingSystem.IsWindows())
            return "pnpm";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in (string[])["pnpm.exe", "pnpm.cmd"])
            {
                var candidate = Path.Combine(directory.Trim('"'), name);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return "pnpm";
    }
}
