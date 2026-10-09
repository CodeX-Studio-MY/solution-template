using Acme.Core.Errors;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Acme.Core.Data;

/// <summary>
/// Fills audit fields from the current principal, turns deletes of soft-deletable entities into updates,
/// and refuses writes for tenants that are not active (suspended tenants are read-only).
/// </summary>
internal sealed class AuditSaveChangesInterceptor(
    TimeProvider time,
    ICurrentPrincipal principal,
    ITenantContext tenant) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
            return;

        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (entries.Count == 0)
            return;

        // Suspended tenants are read-only for their own principals. Platform maintenance (revoking tokens, offboarding,
        // migrations) is done by platform service principals and is not blocked.
        var isPlatformService = principal.Principal is { } user
            && user.GetPrincipalType() == PrincipalType.Service
            && user.HasPermission(PlatformPermissions.TenantAny);
        if (context is ModuleDbContext && tenant.HasTenant && !tenant.Tenant.CanWrite && !isPlatformService)
            throw new ForbiddenException(PlatformErrors.TenantReadOnly);

        var now = time.GetUtcNow().UtcDateTime;
        // When a user is signed in on a machine, the user is recorded; the machine is kept in the audit log.
        var by = principal.Id;

        foreach (var entry in entries)
        {
            if (entry.Entity is IAppendOnly && entry.State != EntityState.Added)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only: rows can't be changed or deleted.");

            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                entry.State = EntityState.Modified;
                softDeletable.MarkDeleted(now);
            }

            if (entry.Entity is not BaseEntity entity)
                continue;
            if (entry.State == EntityState.Added)
                entity.SetCreated(now, by);
            else
                entity.SetUpdated(now, by);
        }
    }
}
