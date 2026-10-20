using Farm360.Core.Data;
using Farm360.Core.Platform;

namespace Farm360.Identity.Domain;

/// <summary>
/// (principal OR group) → module → role. One role per module per assignee: a principal or group can't hold two
/// roles in the same module directly (enforced by filtered unique indexes and a check constraint).
/// </summary>
public sealed class AccessAssignment : BaseEntity
{
    private AccessAssignment()
    {
    }

    private AccessAssignment(Guid? principalId, Guid? groupId, Guid moduleId, Role role)
    {
        PrincipalId = principalId;
        GroupId = groupId;
        ModuleId = moduleId;
        Role = role;
    }

    public Guid? PrincipalId { get; private set; }

    public Guid? GroupId { get; private set; }

    /// <summary>The module's catalog ID. Tokens and code use the module key.</summary>
    public Guid ModuleId { get; private set; }

    public Role Role { get; private set; }

    public static AccessAssignment ForPrincipal(Guid principalId, Guid moduleId, Role role) => new(principalId, null, moduleId, role);

    public static AccessAssignment ForGroup(Guid groupId, Guid moduleId, Role role) => new(null, groupId, moduleId, role);

    public void ChangeRole(Role role) => Role = role;
}

/// <summary>A data scope value (for example a site ID) granted to a principal or a group. Scopes merge as a union.</summary>
public sealed class DataScopeAssignment : BaseEntity
{
    public const int MaxLength = 100;

    private DataScopeAssignment()
    {
    }

    private DataScopeAssignment(Guid? principalId, Guid? groupId, string scope, string value)
    {
        PrincipalId = principalId;
        GroupId = groupId;
        Scope = scope;
        Value = value;
    }

    public Guid? PrincipalId { get; private set; }

    public Guid? GroupId { get; private set; }

    public string Scope { get; private set; } = "";

    public string Value { get; private set; } = "";

    public static DataScopeAssignment ForPrincipal(Guid principalId, string scope, string value) => new(principalId, null, scope, value);

    public static DataScopeAssignment ForGroup(Guid groupId, string scope, string value) => new(null, groupId, scope, value);
}
