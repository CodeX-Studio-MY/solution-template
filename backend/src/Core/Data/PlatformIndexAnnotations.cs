using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Saaskit.Core.Data;

/// <summary>
/// Provider-neutral index options. Modules mark an index; the provider package writes the filter SQL
/// (for example <c>[IsDeleted] = 0</c> on SQL Server, <c>"is_deleted" = false</c> on PostgreSQL).
/// </summary>
public static class PlatformIndexAnnotations
{
    internal const string NotDeleted = "Platform:UniqueAmongNotDeleted";
    internal const string WhereNotNull = "Platform:WhereNotNull";

    /// <summary>A unique index that ignores soft-deleted rows.</summary>
    public static IndexBuilder<T> IsUniqueAmongNotDeleted<T>(this IndexBuilder<T> index) =>
        index.IsUnique().HasAnnotation(NotDeleted, true);

    /// <summary>A unique index over rows where <paramref name="propertyName"/> is set (so NULLs are not treated as duplicates).</summary>
    public static IndexBuilder<T> IsUniqueWhereNotNull<T>(this IndexBuilder<T> index, string propertyName) =>
        index.IsUnique().HasAnnotation(WhereNotNull, propertyName);
}
