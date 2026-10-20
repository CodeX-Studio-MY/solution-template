using Farm360.Core.Errors;
using Microsoft.EntityFrameworkCore;

namespace Farm360.Core.Data;

/// <summary>ETag and If-Match support for aggregates. A conflict returns 412; a missing If-Match returns 428.</summary>
public static class ConcurrencyExtensions
{
    /// <summary>The aggregate's current ETag (a quoted, provider-encoded row version).</summary>
    public static string GetETag<T>(this PlatformDbContext db, T aggregate)
        where T : class, IAggregateRoot
    {
        var token = db.Provider.GetConcurrencyToken(db.Entry(aggregate))
            ?? throw new InvalidOperationException("The aggregate has not been saved yet.");
        return $"\"{token}\"";
    }

    /// <summary>
    /// Applies the client's <c>If-Match</c> value before an update. Throws 428 when it's missing;
    /// <see cref="DbUpdateConcurrencyException"/> (mapped to 412) is raised on save if it no longer matches.
    /// </summary>
    public static void ApplyIfMatch<T>(this PlatformDbContext db, T aggregate, string? ifMatch)
        where T : class, IAggregateRoot
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
            throw new PreconditionRequiredException();
        var token = ifMatch.Trim();
        if (token.StartsWith("W/", StringComparison.Ordinal))
            throw new PreconditionFailedException("Weak ETags are not accepted.");
        db.Provider.SetOriginalConcurrencyToken(db.Entry(aggregate), token.Trim('"'));
    }
}
