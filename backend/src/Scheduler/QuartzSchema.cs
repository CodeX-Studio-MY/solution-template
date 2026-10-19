using System.Text.RegularExpressions;

namespace Saaskit.Scheduler;

/// <summary>
/// Quartz.NET's ADO job-store tables. Quartz ships its own create scripts (idempotent: every statement creates only what is
/// missing); the migrations of <see cref="SchedulerStoreDbContext"/> run them, so the Migrator is the only thing that changes the
/// schema, like every other table. Quartz itself never provisions or alters the schema at runtime (it only validates it).
/// After a Quartz upgrade that changes the schema, add a migration that calls <see cref="CreateStatements"/> again.
/// </summary>
public static partial class QuartzSchema
{
    /// <summary>The prefix to give Quartz: the tables live in the <c>scheduler</c> schema.</summary>
    public const string TablePrefix = $"{SchedulerStoreDbContext.SchemaName}.QRTZ_";

    private const string NamePrefix = "QRTZ_";

    /// <summary>The statements that create (only what is missing of) Quartz's tables, in dependency order.</summary>
    public static IReadOnlyList<string> CreateStatements(bool postgres)
    {
        var script = Script(postgres)
            .Replace("{0}", TablePrefix, StringComparison.Ordinal)
            .Replace("{1}", NamePrefix, StringComparison.Ordinal);
        return StatementSeparator().Split(script)
            .Select(statement => statement.Trim())
            .Where(statement => statement.Split('\n').Any(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal) && line.Trim().Length > 0))
            .ToList();
    }

    /// <summary>The statements that drop the tables again (children first).</summary>
    public static IReadOnlyList<string> DropStatements(bool postgres) =>
        TableNameComment().Matches(Script(postgres))
            .Select(match => $"DROP TABLE IF EXISTS {TablePrefix}{match.Groups[1].Value}")
            .Reverse()
            .ToList();

    private static string Script(bool postgres)
    {
        var assembly = typeof(Quartz.IScheduler).Assembly;
        var name = $"Quartz.Impl.AdoJobStore.Schema.create_{(postgres ? "postgres" : "sqlServer")}.sql";
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Quartz {assembly.GetName().Version} no longer embeds {name}; update QuartzSchema.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>A line reading exactly <c>--;;</c> separates statements (the header's prose mentions it too, mid-line).</summary>
    [GeneratedRegex(@"^--;;\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex StatementSeparator();

    /// <summary>The "-- {0}JOB_DETAILS" comment that precedes each table's statement.</summary>
    [GeneratedRegex(@"^-- \{0\}(\w+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex TableNameComment();
}
