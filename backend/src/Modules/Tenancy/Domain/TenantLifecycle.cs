using Saaskit.Core.Tenancy;

namespace Saaskit.Tenancy.Domain;

/// <summary>
/// The tenant lifecycle: Provisioning → Active → Suspended (read-only) → PendingDeletion → Deleted.
/// Suspension is reversible; offboarding can be cancelled during the grace period; deletion is final.
/// </summary>
public static class TenantLifecycle
{
    private static readonly Dictionary<TenantStatus, TenantStatus[]> Allowed = new()
    {
        [TenantStatus.Provisioning] = [TenantStatus.Active, TenantStatus.PendingDeletion],
        [TenantStatus.Active] = [TenantStatus.Suspended, TenantStatus.PendingDeletion],
        [TenantStatus.Suspended] = [TenantStatus.Active, TenantStatus.PendingDeletion],
        [TenantStatus.PendingDeletion] = [TenantStatus.Active, TenantStatus.Suspended, TenantStatus.Deleted],
        [TenantStatus.Deleted] = [],
    };

    public static bool CanMove(TenantStatus from, TenantStatus to) => Allowed[from].Contains(to);
}
