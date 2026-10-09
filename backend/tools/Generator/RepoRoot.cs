namespace Acme.Generator;

internal static class RepoRoot
{
    /// <summary>The nearest directory, from the current directory upwards, that contains platform.yaml.</summary>
    public static string Find()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "platform.yaml")))
                return dir.FullName;
        }
        throw new PlatformDefinitionException("platform.yaml was not found in this directory or any parent.");
    }
}
