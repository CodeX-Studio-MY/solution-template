using System.Security.Claims;
using Acme.Core.Platform;

namespace Acme.Core.Authorization;

/// <summary>
/// Answers "what may the current principal do" from its effective roles, the tenant's live entitlements and status.
/// Used by handlers and to compute server-driven <c>allowedActions</c>.
/// </summary>
public interface IAccessEvaluator
{
    /// <summary>The principal's effective role in <paramref name="moduleKey"/>, or null when it has none or the tenant isn't entitled.</summary>
    Role? RoleIn(string moduleKey);

    /// <summary>Whether the principal has at least <paramref name="role"/> in <paramref name="moduleKey"/>.</summary>
    bool HasRole(string moduleKey, Role role);

    bool Can(ModulePermission permission);
}

public static class RoleClaims
{
    /// <summary>The effective role per module from the RFC 9068 <c>roles</c> claim (at most one entry per module).</summary>
    public static IReadOnlyDictionary<string, Role> Parse(ClaimsPrincipal principal)
    {
        var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var claim in principal.FindAll(Security.PlatformClaims.Roles))
        {
            if (RoleExtensions.TryParseModuleRole(claim.Value, out var module, out var role)
                && (!roles.TryGetValue(module, out var existing) || role > existing))
            {
                roles[module] = role;
            }
        }
        return roles;
    }
}
