using Saaskit.Core.Http;

namespace Saaskit.Identity.Application;

/// <summary>One audit record, with the names of who did it and what it was done to (when they are people or devices of this tenant).</summary>
public sealed record AuditRecord(
    long Id,
    DateTime OccurredOn,
    string Kind,
    string? ActorId,
    string? ActorName,
    string? ActorType,
    string? OnBehalfOf,
    string? Subject,
    string? SubjectName,
    string? Details,
    string? RemoteAddress);

/// <summary>Reads the tenant's audit log, newest first. Writing stays with <see cref="IAuditWriter"/>; nothing is ever changed.</summary>
public interface IAuditReader
{
    Task<Page<AuditRecord>> ListAsync(PageRequest page, string? kind, CancellationToken cancellationToken = default);
}
