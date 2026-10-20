using Farm360.Core.Platform;

namespace Farm360.Identity.Domain;

/// <summary>A role granted in a module, directly or through a group.</summary>
public sealed record ModuleGrant(Guid ModuleId, Role Role);

/// <summary>A group the principal belongs to, with its grants and scopes.</summary>
public sealed record GroupGrants(bool GroupIsActive, bool MembershipIsActive, IReadOnlyList<ModuleGrant> Grants, IReadOnlyList<ScopeGrant> Scopes);

public sealed record ScopeGrant(string Scope, string Value);

/// <summary>The principal's effective role per module key, and its data scopes.</summary>
public sealed record EffectiveAccess(IReadOnlyDictionary<string, Role> Roles, IReadOnlyDictionary<string, IReadOnlySet<string>> DataScopes)
{
    /// <summary>The RFC 9068 <c>roles</c> claim values: <c>{module-key}:{role}</c>, one per module.</summary>
    public IEnumerable<string> RoleClaims => Roles.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => RoleExtensions.ToModuleRole(r.Key, r.Value));
}

/// <summary>
/// Works out a principal's effective access:
/// 1. collect its active direct assignments and those of every active group it is an active member of;
/// 2. per module keep the highest role (direct and group assignments count equally);
/// 3. remove modules the tenant isn't entitled to (entitlement always overrides assignment);
/// 4. merge data scopes from the principal and its groups as a union.
/// </summary>
public static class EffectiveAccessResolver
{
    public static EffectiveAccess Resolve(
        IEnumerable<ModuleGrant> direct,
        IEnumerable<ScopeGrant> directScopes,
        IEnumerable<GroupGrants> groups,
        IReadOnlySet<string> entitledModuleKeys)
    {
        var activeGroups = groups.Where(g => g.GroupIsActive && g.MembershipIsActive).ToList();
        var keysById = Modules.All.ToDictionary(m => m.Id, m => m.Key);

        var roles = new Dictionary<string, Role>(StringComparer.Ordinal);
        foreach (var grant in direct.Concat(activeGroups.SelectMany(g => g.Grants)))
        {
            if (!keysById.TryGetValue(grant.ModuleId, out var key) || !entitledModuleKeys.Contains(key))
                continue;
            if (!roles.TryGetValue(key, out var held) || grant.Role > held)
                roles[key] = grant.Role;
        }

        var scopes = directScopes.Concat(activeGroups.SelectMany(g => g.Scopes))
            .GroupBy(s => s.Scope, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(s => s.Value).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        return new EffectiveAccess(roles, scopes);
    }
}
