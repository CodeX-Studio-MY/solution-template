using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Core.Data;

/// <summary>
/// The base of every DbContext on the platform. It applies the conventions that keep business code
/// provider-neutral: schema, money precision, soft-delete filters, concurrency tokens and filtered indexes.
/// </summary>
public abstract class PlatformDbContext : DbContext
{
    protected PlatformDbContext(DbContextOptions options)
        : base(options)
    {
        var extension = options.FindExtension<PlatformOptionsExtension>()
            ?? throw new InvalidOperationException(
                $"{GetType().Name} must be registered with AddModuleDbContext or AddCatalogDbContext.");
        Provider = extension.Provider;
        Schema = extension.Schema;
    }

    /// <summary>The schema this context owns. No other context maps it.</summary>
    public string Schema { get; }

    internal IDatabaseProvider Provider { get; }

    /// <summary>Provider-neutral SQL for a check constraint that <paramref name="propertyName"/> matches <paramref name="pattern"/>.</summary>
    protected string MatchesPatternSql(string propertyName, string pattern) => Provider.RegexCheck(propertyName, pattern);

    /// <summary>Provider-neutral SQL for a check constraint that exactly one of two nullable columns is set.</summary>
    protected string ExactlyOneSetSql(string firstProperty, string secondProperty) => Provider.ExactlyOneSetCheck(firstProperty, secondProperty);

    /// <summary>Stores a string property as a native JSON column (provider-neutral: <c>json</c> or <c>jsonb</c>).</summary>
    protected void AsJsonColumn(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string> property) => property.HasColumnType(Provider.JsonColumnType);

    /// <summary>Configures the module's own entities.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigurePlatformModel(modelBuilder);
        ConfigureModel(modelBuilder);
        ApplyPlatformConventions(modelBuilder);
    }

    /// <summary>Entities every context of this kind carries (for example sequence counters).</summary>
    private protected virtual void ConfigurePlatformModel(ModelBuilder modelBuilder)
    {
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
        configurationBuilder.ComplexProperties<Money>();
        // Timestamps are UTC everywhere; values read back are marked as UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        Provider.ConfigureConventions(configurationBuilder);
    }

    private void ApplyPlatformConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => !e.IsOwned()).ToList())
        {
            var clrType = entityType.ClrType;
            var entity = modelBuilder.Entity(clrType);

            if (typeof(ISoftDeletable).IsAssignableFrom(clrType) && entityType.BaseType is null)
            {
                var parameter = Expression.Parameter(clrType, "e");
                var notDeleted = Expression.Lambda(
                    Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted))),
                    parameter);
                entity.HasQueryFilter(QueryFilters.SoftDelete, notDeleted);
            }

            if (typeof(IAggregateRoot).IsAssignableFrom(clrType) && entityType.BaseType is null)
                Provider.ConfigureConcurrencyToken(entity);

            foreach (var index in entityType.GetIndexes())
            {
                var filters = new List<string>();
                if (index.FindAnnotation(PlatformIndexAnnotations.NotDeleted)?.Value is true)
                    filters.Add(Provider.NotDeletedFilter);
                if (index.FindAnnotation(PlatformIndexAnnotations.WhereNotNull)?.Value is string property)
                    filters.Add(Provider.NotNullFilter(property));
                if (filters.Count > 0)
                    index.SetFilter(string.Join(" AND ", filters));
            }
        }
    }
}

/// <summary>Names of the platform's global query filters (EF Core named filters), so callers can disable one.</summary>
public static class QueryFilters
{
    public const string SoftDelete = "SoftDelete";
}

internal sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
    value => UtcGuard.EnsureUtc(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

internal sealed class NullableUtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
    value => value == null ? value : UtcGuard.EnsureUtc(value.Value),
    value => value == null ? value : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

internal static class UtcGuard
{
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new InvalidOperationException("Only UTC timestamps can be stored.");
}
