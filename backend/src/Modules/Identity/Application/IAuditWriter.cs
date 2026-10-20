namespace Farm360.Identity.Application;

/// <summary>Writes the tenant's append-only audit log: who, what, when, from where, and the trace ID.</summary>
public interface IAuditWriter
{
    /// <summary>Records an event in the current tenant's audit log (saved with the caller's next save, or on its own if <paramref name="saveNow"/>).</summary>
    Task WriteAsync(string kind, string? subject = null, object? details = null, string? actorId = null, string? actorType = null, string? onBehalfOf = null, bool saveNow = true, CancellationToken cancellationToken = default);
}
