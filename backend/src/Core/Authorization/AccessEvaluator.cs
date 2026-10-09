using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;

namespace Acme.Core.Authorization;

internal sealed class AccessEvaluator(ICurrentPrincipal principal, ITenantContext tenant) : IAccessEvaluator
{
    private IReadOnlyDictionary<string, Role>? _roles;

    public Role? RoleIn(string moduleKey)
    {
        if (principal.Principal is null || !principal.IsAuthenticated)
            return null;
        // Entitlement always overrides assignment, and is checked live from the catalog cache.
        if (tenant.HasTenant && !tenant.Tenant.EnabledModules.Contains(moduleKey))
            return null;
        _roles ??= RoleClaims.Parse(principal.Principal);
        return _roles.TryGetValue(moduleKey, out var role) ? role : null;
    }

    public bool HasRole(string moduleKey, Role role) => RoleIn(moduleKey) is { } held && held.Includes(role);

    public bool Can(ModulePermission permission) => HasRole(permission.Module, permission.MinimumRole);
}
