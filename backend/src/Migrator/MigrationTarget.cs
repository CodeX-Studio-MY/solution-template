using Acme.Core.Data;

namespace Acme.Migrator;

/// <summary>A DbContext the Migrator applies. The list is generated from platform.yaml (Generated/MigrationTargets.g.cs).</summary>
/// <param name="Name">The service key, or <c>catalog</c> for the tenant directory.</param>
/// <param name="Create">Creates the context for a provider and connection string.</param>
internal sealed record MigrationTarget(string Name, Func<IDatabaseProvider, string, PlatformDbContext> Create);
