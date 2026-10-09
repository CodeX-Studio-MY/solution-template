using Acme.Core.Platform;
using Acme.Core.Security;

namespace Acme.Identity.Application;

public sealed record PrincipalDto(Guid Id, PrincipalType Type, string DisplayName, bool IsActive, string? ClientId, DateTime? LastSeenOn, string ETag);

public sealed record GroupDto(Guid Id, string Name, bool IsActive, int MemberCount, string ETag);

public sealed record AssignmentDto(Guid Id, string Module, Role Role);

public sealed record EffectiveAccessDto(IReadOnlyDictionary<string, Role> Roles, IReadOnlyList<string> RoleClaims);

/// <summary>A one-time machine enrollment code, shown once. Valid 15 minutes, single use.</summary>
public sealed record MachineEnrollmentDto(Guid PrincipalId, string Code, DateTime ExpiresOn);

public sealed record MachineRegistrationDto(Guid PrincipalId, string ClientId);

/// <summary>
/// Users, machines, services, groups and their module roles in the current tenant. Every change raises the affected
/// principals' <c>access_version</c> (for a group change, every member's) and is written to the audit log.
/// </summary>
public interface IAccessManagement
{
    Task<IReadOnlyList<PrincipalDto>> ListPrincipalsAsync(PrincipalType? type, CancellationToken cancellationToken = default);

    Task<PrincipalDto> GetPrincipalAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PrincipalDto> SetPrincipalActiveAsync(Guid id, bool active, string? ifMatch, CancellationToken cancellationToken = default);

    /// <summary>Creates a machine (for example a shared tablet) and its one-time enrollment code.</summary>
    Task<MachineEnrollmentDto> CreateMachineAsync(string displayName, CancellationToken cancellationToken = default);

    /// <summary>Creates a tenant's own service principal for an external integration (ERP, partner API).</summary>
    Task<PrincipalDto> CreateServiceAsync(string displayName, ClientCredential credential, CancellationToken cancellationToken = default);

    Task<MachineEnrollmentDto> NewEnrollmentCodeAsync(Guid machineId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redeems an enrollment code. The device generated its own key pair and registers only the public key,
    /// so the private key never leaves the device.
    /// </summary>
    Task<MachineRegistrationDto> EnrollAsync(string code, string publicKeyJwk, CancellationToken cancellationToken = default);

    Task AddKeyAsync(Guid principalId, string publicKeyJwk, CancellationToken cancellationToken = default);

    Task RemoveKeyAsync(Guid principalId, string keyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupDto>> ListGroupsAsync(CancellationToken cancellationToken = default);

    Task<GroupDto> CreateGroupAsync(string name, CancellationToken cancellationToken = default);

    Task<GroupDto> RenameGroupAsync(Guid id, string name, string? ifMatch, CancellationToken cancellationToken = default);

    Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddMemberAsync(Guid groupId, Guid principalId, CancellationToken cancellationToken = default);

    Task RemoveMemberAsync(Guid groupId, Guid principalId, CancellationToken cancellationToken = default);

    /// <summary>Sets the single role of a principal or group in a module (replacing any existing one).</summary>
    Task<AssignmentDto> SetPrincipalRoleAsync(Guid principalId, string moduleKey, Role role, CancellationToken cancellationToken = default);

    Task RemovePrincipalRoleAsync(Guid principalId, string moduleKey, CancellationToken cancellationToken = default);

    Task<AssignmentDto> SetGroupRoleAsync(Guid groupId, string moduleKey, Role role, CancellationToken cancellationToken = default);

    Task RemoveGroupRoleAsync(Guid groupId, string moduleKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignmentDto>> ListPrincipalAssignmentsAsync(Guid principalId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignmentDto>> ListGroupAssignmentsAsync(Guid groupId, CancellationToken cancellationToken = default);

    Task<EffectiveAccessDto> GetEffectiveAccessAsync(Guid principalId, CancellationToken cancellationToken = default);
}
