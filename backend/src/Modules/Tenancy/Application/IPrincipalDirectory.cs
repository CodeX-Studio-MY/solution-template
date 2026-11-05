using Saaskit.Core.Http;
using Saaskit.Tenancy.Domain;

namespace Saaskit.Tenancy.Application;

/// <summary>What creates or changes a principal. Only the fields of its <see cref="Type"/> are used.</summary>
public sealed record PrincipalInput(
    PlatformPrincipalType Type,
    string DisplayName,
    string? Email = null,
    DeviceType? DeviceType = null,
    string? Identifier = null,
    string? Manufacturer = null,
    string? Model = null,
    bool? IsExternal = null,
    string? OwnerName = null,
    string? ContactEmail = null);

/// <summary>One row of the principal list. <see cref="Identifier"/> is the email, the device's identifier or the application's owner.</summary>
public sealed record PrincipalSummary(Guid PrincipalId, PlatformPrincipalType Type, string DisplayName, bool IsActive, string? Identifier, DeviceType? DeviceType, bool? IsExternal, int TenantCount);

public sealed record PrincipalTenantAccess(Guid TenantId, string TenantSlug, string TenantName, TenantAccessStatus Status, DateTime GrantedOn);

public sealed record LinkedProvider(LoginProvider Provider, string? ProviderEmail, DateTime LinkedOn);

public sealed record PrincipalDetail(
    Guid PrincipalId,
    PlatformPrincipalType Type,
    string DisplayName,
    bool IsActive,
    string? Email,
    DeviceType? DeviceType,
    string? Identifier,
    string? Manufacturer,
    string? Model,
    bool? IsExternal,
    string? OwnerName,
    string? ContactEmail,
    IReadOnlyList<PrincipalTenantAccess> Tenants,
    IReadOnlyList<LinkedProvider> Providers,
    string ETag);

/// <summary>One principal's access to a tenant, as the tenant's page lists it.</summary>
public sealed record TenantPrincipalRow(Guid PrincipalId, PlatformPrincipalType Type, string DisplayName, bool IsActive, string? Identifier, DeviceType? DeviceType, bool? IsExternal, TenantAccessStatus Status, DateTime GrantedOn, string? GrantedByName);

public sealed record AccessAuditRow(long Id, DateTime OccurredOn, Guid TenantId, string? TenantName, Guid PrincipalId, string? PrincipalName, TenantAccessAction Action, TenantAccessStatus? FromStatus, TenantAccessStatus ToStatus, Guid ActorId, string? ActorName, string? Reason, string? RemoteAddress);

public sealed record LoginRow(long Id, DateTime OccurredOn, Guid? PrincipalId, string? PrincipalName, PlatformPrincipalType? PrincipalType, bool Succeeded, LoginMethod Method, LoginFailureReason? FailureReason, string? RemoteAddress, string? UserAgent);

/// <summary>
/// The catalog's users, devices and applications, and which tenants each may access. These records don't control sign-in yet:
/// tokens and roles still come from each tenant's own database.
/// </summary>
public interface IPrincipalDirectory
{
    Task<Page<PrincipalSummary>> ListAsync(PlatformPrincipalType? type, string? search, PageRequest page, CancellationToken cancellationToken = default);

    Task<PrincipalDetail> GetAsync(Guid principalId, CancellationToken cancellationToken = default);

    Task<PrincipalDetail> CreateAsync(PrincipalInput input, Guid actor, CancellationToken cancellationToken = default);

    Task<PrincipalDetail> UpdateAsync(Guid principalId, PrincipalInput input, string? ifMatch, Guid actor, CancellationToken cancellationToken = default);

    Task<PrincipalDetail> SetActiveAsync(Guid principalId, bool active, string? ifMatch, Guid actor, CancellationToken cancellationToken = default);

    Task<Page<TenantPrincipalRow>> ListForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Gives access, or gives it back after a revoke. Already active is a no-op; suspended access is resumed instead.</summary>
    Task<TenantPrincipalRow> GrantAsync(Guid tenantId, Guid principalId, string? reason, Guid actor, CancellationToken cancellationToken = default);

    Task<TenantPrincipalRow> SetStatusAsync(Guid tenantId, Guid principalId, TenantAccessStatus status, string reason, Guid actor, CancellationToken cancellationToken = default);

    Task<Page<AccessAuditRow>> ListAuditForTenantAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);

    Task<Page<AccessAuditRow>> ListAuditForPrincipalAsync(Guid principalId, Guid? tenantId, PageRequest page, CancellationToken cancellationToken = default);

    Task<Page<LoginRow>> ListLoginsAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);
}
