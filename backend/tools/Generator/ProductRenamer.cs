using System.Text;

namespace Farm360.Generator;

/// <summary>
/// The one-time codemod that names the product: every <c>farm360</c>, <c>Farm360</c> and <c>FARM360</c> in the repository's text files and
/// paths becomes the new name in the same casing. C# namespaces and npm package scopes can't be read from platform.yaml, so they
/// are renamed once, when a product is scaffolded from the template (see deviations 4 and 42).
/// It checks that every file it will change can be opened before it changes any, so a file another program holds (an IDE with the
/// solution open) stops it before it starts rather than half-way. Running it again on a renamed tree changes nothing, and on a
/// half-renamed one finishes the job.
/// </summary>
internal static class ProductRenamer
{
    /// <summary>Build output, dependencies and local state: never part of the source.</summary>
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".dev", ".turbo", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "TestResults", "test-results", "playwright-report",
    };

    /// <summary>How often a locked file is tried again, and how long to wait between tries.</summary>
    private const int Attempts = 5;

    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(400);

    /// <summary>What a rename changed: the files whose text changed and the paths that were renamed.</summary>
    public sealed record Result(IReadOnlyList<string> ChangedFiles, IReadOnlyList<(string From, string To)> RenamedPaths);

    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> (both product keys, lowercase) under <paramref name="root"/>.</summary>
    public static Result Run(string root, string from, string to)
    {
        if (from == to)
            return new Result([], []);
        var replacements = Replacements(from, to);

        // 1. Everything that will change, worked out in memory.
        var edits = new List<(string Path, string Text, bool Bom)>();
        foreach (var file in SourceFiles(root))
        {
            var bytes = Array.Empty<byte>();
            Retry(file, () => bytes = File.ReadAllBytes(file));
            if (IsBinary(bytes))
                continue;
            var hasBom = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            var text = Encoding.UTF8.GetString(hasBom ? bytes[3..] : bytes);
            var renamed = Replace(text, replacements);
            if (renamed != text)
                edits.Add((file, renamed, hasBom));
        }
        // Deepest first, so a renamed directory never invalidates a path still to be renamed below it.
        var moves = Directory.EnumerateFileSystemEntries(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(path => !IsSkipped(root, path) && Replace(Path.GetFileName(path), replacements) != Path.GetFileName(path))
            .OrderByDescending(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();

        // 2. Nothing is touched unless every file can be: another program holding one fails here, with its name.
        foreach (var path in edits.Select(e => e.Path).Concat(moves.Where(File.Exists)).Distinct(StringComparer.OrdinalIgnoreCase))
            Retry(path, () => { using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });

        // 3. The changes.
        foreach (var (path, text, bom) in edits)
            Retry(path, () => File.WriteAllText(path, text, new UTF8Encoding(bom)));
        var renamedPaths = new List<(string, string)>();
        foreach (var path in moves)
        {
            var target = Path.Combine(Path.GetDirectoryName(path)!, Replace(Path.GetFileName(path), replacements));
            Retry(path, () =>
            {
                if (Directory.Exists(path))
                    Directory.Move(path, target);
                else
                    File.Move(path, target);
            });
            renamedPaths.Add((Path.GetRelativePath(root, path), Path.GetRelativePath(root, target)));
        }
        return new Result([.. edits.Select(e => Path.GetRelativePath(root, e.Path))], renamedPaths);
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

    /// <summary>Runs <paramref name="action"/>, trying again while another program holds <paramref name="path"/>.</summary>
    private static void Retry(string path, Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == Attempts)
                    throw new PlatformDefinitionException(
                        $"{path} is in use by another program ({ex.Message.TrimEnd('.')}). Close it (an IDE such as VS Code or Visual Studio with " +
                        "the solution open is the usual one), then run scripts/generate again: it continues where it stopped.");
                Thread.Sleep(Pause);
            }
        }
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
