using Saaskit.Generator.Emitters;

namespace Saaskit.Generator.Tests;

/// <summary>deploy/.env.example is generated from the options records; this proves the committed file and the schema agree both ways.</summary>
public sealed class EnvExampleTests
{
    [Fact]
    public void Every_key_in_env_example_is_in_the_schema_and_every_schema_key_is_in_env_example()
    {
        var path = Path.Combine(RepoRoot(), "deploy", ".env.example");
        var inFile = File.ReadAllLines(path)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line[..line.IndexOf('=', StringComparison.Ordinal)])
            .ToHashSet(StringComparer.Ordinal);
        var inSchema = ConfigurationEmitter.Sections().SelectMany(section => section.Settings.Select(section.EnvName)).ToHashSet(StringComparer.Ordinal);

        inFile.Except(inSchema).ShouldBeEmpty("keys in .env.example that no options record defines");
        inSchema.Except(inFile).ShouldBeEmpty("keys in the schema that .env.example lacks");
        inSchema.ShouldNotBeEmpty();
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "platform.yaml")))
                return dir.FullName;
        }
        throw new InvalidOperationException("platform.yaml was not found above the test output.");
    }
}
