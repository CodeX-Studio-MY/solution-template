using System.Text.Json;
using Saaskit.Core.Http;
using Saaskit.Identity.Domain;
using Saaskit.Identity.Application;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Identity.Infrastructure;

internal sealed class AuditReader(IdentityDbContext db) : IAuditReader
{
    public async Task<Page<AuditRecord>> ListAsync(PageRequest page, string? kind, CancellationToken cancellationToken = default)
    {
        var query = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(e => e.Kind == kind);
        var entries = await query.OrderByDescending(e => e.Id).ToPageAsync(page, cancellationToken);

        // Actors and subjects are principal IDs as text; name the ones this tenant has (one query for the page).
        var ids = entries.Items.SelectMany(e => new[] { e.ActorId, e.Subject })
            .Select(v => Guid.TryParse(v, out var id) ? id : (Guid?)null).OfType<Guid>().Distinct().ToList();
        var names = await db.Principals.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id.ToString(), p => p.DisplayName, cancellationToken);
        string? Name(string? id) => id is not null && Guid.TryParse(id, out var guid) ? names.GetValueOrDefault(guid.ToString()) : null;

        // Operators are not principals of the tenant; their name is in the entry's details (support access).
        string? ActorName(AuditEntry e) => e.ActorType == "operator" ? OperatorName(e.Details) : Name(e.ActorId);

        return entries.Map(e => new AuditRecord(e.Id, e.OccurredOn, e.Kind, e.ActorId, ActorName(e), e.ActorType, e.OnBehalfOf, e.Subject, Name(e.Subject), e.Details, e.RemoteAddress));
    }

    private static string? OperatorName(string? details)
    {
        if (details is null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(details);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("operator", out var name) && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
