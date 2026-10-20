namespace Farm360.Generator;

/// <summary>
/// Whether a new module key already appears as a string in the platform's hand-written code (for example <c>"master"</c>, SQL Server's
/// system database). The architecture test <c>Module_and_service_keys_are_not_hard_coded</c> would then fail on code that has nothing
/// to do with the module, so the key is refused before anything is created. Same files and rule as that test.
/// </summary>
internal static class KeyCollisions
{
    /// <summary>The places in backend/src where <paramref name="key"/> is written as a string, as <c>path:line</c>.</summary>
    public static IReadOnlyList<string> Find(string repoRoot, string key)
    {
        var source = Path.Combine(repoRoot, "backend", "src");
        if (!Directory.Exists(source))
            return [];
        return Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(source, path).Replace('\\', '/')))
            .Where(file => !file.Relative.Contains("/bin/", StringComparison.Ordinal)
                && !file.Relative.Contains("/obj/", StringComparison.Ordinal)
                && !file.Relative.Contains("/Generated/", StringComparison.Ordinal)
                && !file.Relative.Contains("/Migrations/", StringComparison.Ordinal)
                && !file.Relative.EndsWith(".g.cs", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file.Path).Select((line, i) => (file.Relative, Line: line, Number: i + 1)))
            .Where(x => !x.Line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                && (x.Line.Contains($"\"{key}\"", StringComparison.Ordinal) || x.Line.Contains($"\"{key}:", StringComparison.Ordinal)))
            .Select(x => $"{x.Relative}:{x.Number}")
            .ToList();
    }

    /// <summary>Fails with the places it was found, when <paramref name="key"/> collides.</summary>
    public static void Check(string repoRoot, string key)
    {
        var found = Find(repoRoot, key);
        if (found.Count > 0)
            throw new PlatformDefinitionException(
                $"'{key}' can't be a module key: the platform already uses it as a name in {string.Join(", ", found.Take(3))}. Choose another key (for example '{key}-data').");
    }
}
