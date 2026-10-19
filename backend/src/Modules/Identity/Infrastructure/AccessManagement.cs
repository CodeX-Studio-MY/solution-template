using System.Security.Cryptography;
using Saaskit.Core.Data;
using Saaskit.Core.Errors;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Core.Tenancy;
using Saaskit.Identity.Application;
using Saaskit.Identity.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Saaskit.Identity.Infrastructure;

internal sealed class AccessManagement(
    IdentityDbContext db,
    ITenantContext tenant,
    IAccessVersionStore versions,
    AccessPrincipalFactory principals,
    IClientRegistry clients,
    IAuditWriter audit,
    TimeProvider time) : IAccessManagement
{
    // ---- Principals -----------------------------------------------------------------------------

    public async Task<IReadOnlyList<PrincipalDto>> ListPrincipalsAsync(PrincipalType? type, CancellationToken cancellationToken = default)
    {
        var query = db.Principals.AsNoTracking();
        if (type is not null)
            query = query.Where(p => p.Type == type);
        var principals = await query.OrderBy(p => p.DisplayName).ToListAsync(cancellationToken);
        return [.. principals.Select(ToDto)];
    }

    public async Task<PrincipalDto> GetPrincipalAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToDto(await FindPrincipalAsync(id, cancellationToken));

    public async Task<PrincipalDto> SetPrincipalActiveAsync(Guid id, bool active, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var principal = await FindPrincipalAsync(id, cancellationToken);
        db.ApplyIfMatch(principal, ifMatch);
        if (active)
            principal.Enable();
        else
            principal.Disable();
        await audit.WriteAsync(AuditKinds.PrincipalChanged, subject: id.ToString(), details: new { active }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync([principal], cancellationToken);
        return ToDto(principal);
    }

    public async Task<MachineEnrollmentDto> CreateMachineAsync(string displayName, CancellationToken cancellationToken = default)
    {
        var machine = Principal.NewMachine(displayName);
        db.Principals.Add(machine);
        await db.SaveChangesAsync(cancellationToken);
        return await IssueEnrollmentAsync(machine, cancellationToken);
    }

    public async Task<PrincipalDto> CreateServiceAsync(string displayName, ClientCredential credential, CancellationToken cancellationToken = default)
    {
        var service = Principal.NewService(displayName);
        var clientId = ClientIdFor(service);
        service.AttachClient(clientId);
        db.Principals.Add(service);
        await audit.WriteAsync(AuditKinds.PrincipalChanged, subject: service.Id.ToString(), details: new { created = "service" }, saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await clients.RegisterAsync(clientId, displayName, tenant.Tenant.Id, credential, ["client_credentials"], cancellationToken);
        return ToDto(service);
    }

    public async Task<MachineEnrollmentDto> NewEnrollmentCodeAsync(Guid machineId, CancellationToken cancellationToken = default)
    {
        var machine = await FindPrincipalAsync(machineId, cancellationToken);
        if (machine.Type != PrincipalType.Machine)
            throw new BusinessRuleException(IdentityErrors.EnrollmentInvalid, "Only machines enroll.");
        return await IssueEnrollmentAsync(machine, cancellationToken);
    }

    public async Task<MachineRegistrationDto> EnrollAsync(string code, string publicKeyJwk, CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var hash = AccountService.Hash(code);
        var enrollment = await db.MachineEnrollments.SingleOrDefaultAsync(e => e.CodeHash == hash, cancellationToken);
        if (enrollment is null || !enrollment.CanUse(now))
            throw new BusinessRuleException(IdentityErrors.EnrollmentInvalid);

        var machine = await FindPrincipalAsync(enrollment.PrincipalId, cancellationToken);
        var clientId = ClientIdFor(machine);
        enrollment.Use(now);
        machine.AttachClient(clientId);
        machine.Enable();
        await audit.WriteAsync(AuditKinds.MachineEnrolled, subject: machine.Id.ToString(), actorId: machine.Id.ToString(), actorType: PrincipalType.Machine.ToClaimValue(), saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        // Only the public key is registered. The device kept its private key.
        await clients.RegisterAsync(clientId, machine.DisplayName, tenant.Tenant.Id, new ClientCredential(PublicKeyJwk: publicKeyJwk), ["client_credentials"], cancellationToken);
        return new MachineRegistrationDto(machine.Id, clientId);
    }

    public async Task AddKeyAsync(Guid principalId, string publicKeyJwk, CancellationToken cancellationToken = default)
    {
        var principal = await FindClientPrincipalAsync(principalId, cancellationToken);
        await clients.AddKeyAsync(principal.ClientId!, publicKeyJwk, cancellationToken);
        await audit.WriteAsync(AuditKinds.KeyRotated, subject: principalId.ToString(), details: new { added = true }, cancellationToken: cancellationToken);
    }

    public async Task RemoveKeyAsync(Guid principalId, string keyId, CancellationToken cancellationToken = default)
    {
        var principal = await FindClientPrincipalAsync(principalId, cancellationToken);
        await clients.RemoveKeyAsync(principal.ClientId!, keyId, cancellationToken);
        await audit.WriteAsync(AuditKinds.KeyRotated, subject: principalId.ToString(), details: new { removed = keyId }, cancellationToken: cancellationToken);
    }

    // ---- Groups ---------------------------------------------------------------------------------

    public async Task<IReadOnlyList<GroupDto>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new { Group = g, Members = db.GroupMemberships.Count(m => m.GroupId == g.Id && m.IsActive) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new GroupDto(r.Group.Id, r.Group.Name, r.Group.IsActive, r.Members, ETagOf(r.Group)))];
    }

    public async Task<GroupDto> CreateGroupAsync(string name, CancellationToken cancellationToken = default)
    {
        var group = new Group(name);
        if (await db.Groups.AnyAsync(g => g.NormalizedName == group.NormalizedName, cancellationToken))
            throw new ConflictException(IdentityErrors.GroupNameInUse);
        db.Groups.Add(group);
        await audit.WriteAsync(AuditKinds.GroupChanged, subject: group.Id.ToString(), details: new { created = group.Name }, saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new GroupDto(group.Id, group.Name, group.IsActive, 0, ETagOf(group));
    }

    public async Task<GroupDto> RenameGroupAsync(Guid id, string name, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var group = await FindGroupAsync(id, cancellationToken);
        db.ApplyIfMatch(group, ifMatch);
        group.Rename(name);
        if (await db.Groups.AnyAsync(g => g.NormalizedName == group.NormalizedName && g.Id != id, cancellationToken))
            throw new ConflictException(IdentityErrors.GroupNameInUse);
        await audit.WriteAsync(AuditKinds.GroupChanged, subject: id.ToString(), details: new { renamed = group.Name }, saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new GroupDto(group.Id, group.Name, group.IsActive, await db.GroupMemberships.CountAsync(m => m.GroupId == id && m.IsActive, cancellationToken), ETagOf(group));
    }

    public async Task DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await FindGroupAsync(id, cancellationToken);
        var members = await MembersOfAsync(id, cancellationToken);
        db.Groups.Remove(group);   // soft delete
        await audit.WriteAsync(AuditKinds.GroupChanged, subject: id.ToString(), details: new { deleted = true }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync(members, cancellationToken);
    }

    public async Task AddMemberAsync(Guid groupId, Guid principalId, CancellationToken cancellationToken = default)
    {
        await FindGroupAsync(groupId, cancellationToken);
        var principal = await FindPrincipalAsync(principalId, cancellationToken);
        if (principal.Type == PrincipalType.Service)
            throw new BusinessRuleException(IdentityErrors.ServicePrincipalsHaveNoGroups);

        var membership = await db.GroupMemberships.SingleOrDefaultAsync(m => m.GroupId == groupId && m.PrincipalId == principalId, cancellationToken);
        if (membership is null)
            db.GroupMemberships.Add(new GroupMembership(groupId, principalId));
        else
            membership.Activate();
        await audit.WriteAsync(AuditKinds.MembershipChanged, subject: principalId.ToString(), details: new { groupId, added = true }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync([principal], cancellationToken);
    }

    public async Task RemoveMemberAsync(Guid groupId, Guid principalId, CancellationToken cancellationToken = default)
    {
        var principal = await FindPrincipalAsync(principalId, cancellationToken);
        var membership = await db.GroupMemberships.SingleOrDefaultAsync(m => m.GroupId == groupId && m.PrincipalId == principalId, cancellationToken);
        if (membership is null)
            return;
        db.GroupMemberships.Remove(membership);
        await audit.WriteAsync(AuditKinds.MembershipChanged, subject: principalId.ToString(), details: new { groupId, removed = true }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync([principal], cancellationToken);
    }

    // ---- Role assignments -----------------------------------------------------------------------

    public async Task<AssignmentDto> SetPrincipalRoleAsync(Guid principalId, string moduleKey, Role role, CancellationToken cancellationToken = default)
    {
        var principal = await FindPrincipalAsync(principalId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        var assignment = await db.AccessAssignments.SingleOrDefaultAsync(a => a.PrincipalId == principalId && a.ModuleId == moduleId, cancellationToken);
        if (assignment is null)
        {
            assignment = AccessAssignment.ForPrincipal(principalId, moduleId, role);
            db.AccessAssignments.Add(assignment);
        }
        else
        {
            assignment.ChangeRole(role);
        }
        await audit.WriteAsync(AuditKinds.AssignmentChanged, subject: principalId.ToString(), details: new { module = moduleKey, role = role.ToClaimValue() }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync([principal], cancellationToken);
        return new AssignmentDto(assignment.Id, moduleKey, role);
    }

    public async Task RemovePrincipalRoleAsync(Guid principalId, string moduleKey, CancellationToken cancellationToken = default)
    {
        var principal = await FindPrincipalAsync(principalId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        var assignment = await db.AccessAssignments.SingleOrDefaultAsync(a => a.PrincipalId == principalId && a.ModuleId == moduleId, cancellationToken);
        if (assignment is null)
            return;
        db.AccessAssignments.Remove(assignment);
        await audit.WriteAsync(AuditKinds.AssignmentChanged, subject: principalId.ToString(), details: new { module = moduleKey, removed = true }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync([principal], cancellationToken);
    }

    public async Task<AssignmentDto> SetGroupRoleAsync(Guid groupId, string moduleKey, Role role, CancellationToken cancellationToken = default)
    {
        await FindGroupAsync(groupId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        var assignment = await db.AccessAssignments.SingleOrDefaultAsync(a => a.GroupId == groupId && a.ModuleId == moduleId, cancellationToken);
        if (assignment is null)
        {
            assignment = AccessAssignment.ForGroup(groupId, moduleId, role);
            db.AccessAssignments.Add(assignment);
        }
        else
        {
            assignment.ChangeRole(role);
        }
        var members = await MembersOfAsync(groupId, cancellationToken);
        await audit.WriteAsync(AuditKinds.AssignmentChanged, subject: groupId.ToString(), details: new { group = true, module = moduleKey, role = role.ToClaimValue() }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync(members, cancellationToken);   // a group change affects every member
        return new AssignmentDto(assignment.Id, moduleKey, role);
    }

    public async Task RemoveGroupRoleAsync(Guid groupId, string moduleKey, CancellationToken cancellationToken = default)
    {
        await FindGroupAsync(groupId, cancellationToken);
        var moduleId = ModuleId(moduleKey);
        var assignment = await db.AccessAssignments.SingleOrDefaultAsync(a => a.GroupId == groupId && a.ModuleId == moduleId, cancellationToken);
        if (assignment is null)
            return;
        db.AccessAssignments.Remove(assignment);
        var members = await MembersOfAsync(groupId, cancellationToken);
        await audit.WriteAsync(AuditKinds.AssignmentChanged, subject: groupId.ToString(), details: new { group = true, module = moduleKey, removed = true }, saveNow: false, cancellationToken: cancellationToken);
        await SaveAndPublishAsync(members, cancellationToken);
    }

    public async Task<IReadOnlyList<AssignmentDto>> ListPrincipalAssignmentsAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        await FindPrincipalAsync(principalId, cancellationToken);
        var rows = await db.AccessAssignments.AsNoTracking().Where(a => a.PrincipalId == principalId).ToListAsync(cancellationToken);
        return [.. rows.Select(ToDto).OrderBy(a => a.Module, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<AssignmentDto>> ListGroupAssignmentsAsync(Guid groupId, CancellationToken cancellationToken = default)
    {
        await FindGroupAsync(groupId, cancellationToken);
        var rows = await db.AccessAssignments.AsNoTracking().Where(a => a.GroupId == groupId).ToListAsync(cancellationToken);
        return [.. rows.Select(ToDto).OrderBy(a => a.Module, StringComparer.Ordinal)];
    }

    public async Task<ContactDto?> GetContactAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        var row = await (from p in db.Principals.AsNoTracking()
                         join u in db.Users.AsNoTracking() on p.Id equals u.Id into users
                         from user in users.DefaultIfEmpty()
                         where p.Id == principalId && p.IsActive && p.Type == PrincipalType.User
                         select new { p.Id, p.DisplayName, Email = user == null ? null : user.Email }).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new ContactDto(row.Id, row.DisplayName, row.Email);
    }

    public async Task<IReadOnlyList<ContactDto>> ListModuleMembersAsync(string moduleKey, Role minimumRole, CancellationToken cancellationToken = default)
    {
        _ = ModuleId(moduleKey);
        var users = await (from p in db.Principals.AsNoTracking()
                           join u in db.Users.AsNoTracking() on p.Id equals u.Id
                           where p.IsActive && p.Type == PrincipalType.User
                           select new ContactDto(p.Id, p.DisplayName, u.Email)).ToListAsync(cancellationToken);
        var members = new List<ContactDto>();
        foreach (var user in users)
        {
            var access = await principals.ResolveAsync(user.Id, cancellationToken);
            if (access.Roles.TryGetValue(moduleKey, out var role) && role >= minimumRole)
                members.Add(user);
        }
        return members;
    }

    public async Task<EffectiveAccessDto> GetEffectiveAccessAsync(Guid principalId, CancellationToken cancellationToken = default)
    {
        await FindPrincipalAsync(principalId, cancellationToken);
        var access = await principals.ResolveAsync(principalId, cancellationToken);
        return new EffectiveAccessDto(access.Roles, [.. access.RoleClaims]);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    /// <summary>Saves, then raises the <c>access_version</c> of every affected principal (in the database and in Redis).</summary>
    private async Task SaveAndPublishAsync(IReadOnlyCollection<Principal> affected, CancellationToken cancellationToken)
    {
        foreach (var principal in affected)
            principal.BumpAccessVersion();
        await db.SaveChangesAsync(cancellationToken);
        await versions.SetAsync(
            tenant.Tenant.Id,
            affected.ToDictionary(p => p.Id.ToString(), p => p.AccessVersion),
            cancellationToken);
    }

    private async Task<List<Principal>> MembersOfAsync(Guid groupId, CancellationToken cancellationToken) =>
        await (from membership in db.GroupMemberships
               join principal in db.Principals on membership.PrincipalId equals principal.Id
               where membership.GroupId == groupId
               select principal).ToListAsync(cancellationToken);

    private async Task<MachineEnrollmentDto> IssueEnrollmentAsync(Principal machine, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var code = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        var expires = now.Add(MachineEnrollment.Lifetime);
        db.MachineEnrollments.Add(new MachineEnrollment(machine.Id, AccountService.Hash(code), expires));
        await audit.WriteAsync(AuditKinds.PrincipalChanged, subject: machine.Id.ToString(), details: new { enrollmentCodeIssued = true }, saveNow: false, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new MachineEnrollmentDto(machine.Id, code, expires);
    }

    private async Task<Principal> FindPrincipalAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Principals.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException(IdentityErrors.PrincipalNotFound);

    private async Task<Principal> FindClientPrincipalAsync(Guid id, CancellationToken cancellationToken)
    {
        var principal = await FindPrincipalAsync(id, cancellationToken);
        return principal.ClientId is null || principal.Type == PrincipalType.User
            ? throw new BusinessRuleException(IdentityErrors.EnrollmentInvalid, "Only enrolled machines and services have keys.")
            : principal;
    }

    private async Task<Group> FindGroupAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Groups.SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
        ?? throw new NotFoundException(IdentityErrors.GroupNotFound);

    private static Guid ModuleId(string moduleKey) =>
        Modules.All.SingleOrDefault(m => m.Key == moduleKey)?.Id ?? throw new BusinessRuleException(IdentityErrors.UnknownModule, moduleKey);

    private string ClientIdFor(Principal principal) => $"{tenant.Tenant.Slug}-{principal.Type.ToClaimValue()}-{principal.Id:N}";

    private PrincipalDto ToDto(Principal p) => new(p.Id, p.Type, p.DisplayName, p.IsActive, p.ClientId, p.LastSeenOn, db.GetETag(p));

    private string ETagOf(Group g) => db.GetETag(g);

    private static AssignmentDto ToDto(AccessAssignment a) =>
        new(a.Id, Modules.All.Single(m => m.Id == a.ModuleId).Key, a.Role);
}
