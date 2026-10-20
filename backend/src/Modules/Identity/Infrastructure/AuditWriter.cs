using System.Diagnostics;
using System.Text.Json;
using Farm360.Core.Security;
using Farm360.Identity.Application;
using Farm360.Identity.Domain;
using Microsoft.AspNetCore.Http;

namespace Farm360.Identity.Infrastructure;

internal sealed class AuditWriter(IdentityDbContext db, ICurrentPrincipal principal, IHttpContextAccessor http, TimeProvider time) : IAuditWriter
{
    public async Task WriteAsync(string kind, string? subject = null, object? details = null, string? actorId = null, string? actorType = null, string? onBehalfOf = null, bool saveNow = true, CancellationToken cancellationToken = default)
    {
        db.AuditLog.Add(new AuditEntry(
            time.GetUtcNow().UtcDateTime,
            kind,
            actorId ?? principal.Id,
            actorType ?? principal.Type?.ToString().ToLowerInvariant(),
            onBehalfOf ?? principal.ActorId,
            subject,
            details is null ? null : JsonSerializer.Serialize(details),
            http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Activity.Current?.TraceId.ToString()));
        if (saveNow)
            await db.SaveChangesAsync(cancellationToken);
    }
}
