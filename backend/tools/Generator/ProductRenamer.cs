using System.Text;

namespace Saaskit.Generator;

/// <summary>
/// The one-time codemod that names the product: every <c>saaskit</c>, <c>Saaskit</c> and <c>SAASKIT</c> in the repository's text files and
/// paths becomes the new name in the same casing. C# namespaces and npm package scopes can't be read from platform.yaml, so they
/// are renamed once, when a product is scaffolded from the template (see deviations 4 and 42).
/// </summary>
internal static class ProductRenamer
{
    /// <summary>Build output, dependencies and local state: never part of the source.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".dev", ".turbo", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "TestResults", "test-results", "playwright-report",
    };

    /// <summary>What a rename changed: the files whose text changed and the paths that were renamed.</summary>
    public sealed record Result(IReadOnlyList<string> ChangedFiles, IReadOnlyList<(string From, string To)> RenamedPaths);

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> (both product keys, lowercase) under <paramref name="root"/>.</summary>
    public static Result Run(string root, string from, string to)
    {
        if (from == to)
            return new Result([], []);
        var replacements = Replacements(from, to);

        var changed = new List<string>();
        foreach (var file in SourceFiles(root))
        {
            var bytes = File.ReadAllBytes(file);
            if (IsBinary(bytes))
                continue;
            var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            var text = Encoding.UTF8.GetString(hasBom ? bytes[3..] : bytes);
            var renamed = Replace(text, replacements);
            if (renamed == text)
                continue;
            File.WriteAllText(file, renamed, new UTF8Encoding(hasBom));
            changed.Add(Path.GetRelativePath(root, file));
        }

        // Deepest first, so a renamed directory never invalidates a path still to be renamed below it.
        var renamedPaths = new List<(string, string)>();
        var candidates = Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(path => !IsSkipped(root, path) && Replace(Path.GetFileName(path), replacements) != Path.GetFileName(path))
            .OrderByDescending(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();
        foreach (var path in candidates)
        {
            var target = Path.Combine(Path.GetDirectoryName(path)!, Replace(Path.GetFileName(path), replacements));
            if (Directory.Exists(path))
                Directory.Move(path, target);
            else
                File.Move(path, target);
            renamedPaths.Add((Path.GetRelativePath(root, path), Path.GetRelativePath(root, target)));
        }
        return new Result(changed, renamedPaths);
    }

    /// <summary>The three spellings in the order they are applied: PascalCase, lowercase, UPPERCASE.</summary>
    internal static IReadOnlyList<(string From, string To)> Replacements(string from, string to) =>
    [
        (Naming.Pascal(from), Naming.Pascal(to)),
        (from, to),
        (from.ToUpperInvariant(), to.ToUpperInvariant()),
    ];

    internal static string Replace(string text, IReadOnlyList<(string From, string To)> replacements)
    {
        foreach (var (from, to) in replacements)
            text = text.Replace(from, to, StringComparison.Ordinal);
        return text;
    }

    private static IEnumerable<string> SourceFiles(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(path => !IsSkipped(root, path));

    private static bool IsSkipped(string root, string path) =>
        Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).SkipLast(1).Any(SkippedDirectories.Contains)
        || (Directory.Exists(path) && SkippedDirectories.Contains(Path.GetFileName(path)));

    /// <summary>A NUL byte in the first 8 KB: images, fonts, archives. Text files never contain one.</summary>
    private static bool IsBinary(byte[] bytes) => bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0);
}
