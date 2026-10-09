using Acme.Core.Data;
using Acme.Core.Platform;

namespace Acme.Tenancy.Domain;

/// <summary>
/// A platform operator: our own support staff. A separate realm from every tenant's users, stored in the catalog.
/// MFA (a TOTP code) is always required. Operators reach a tenant only through time-limited, audited support access.
/// </summary>
public sealed class PlatformOperator : BaseEntity, IAggregateRoot
{
    private PlatformOperator()
    {
    }

    public PlatformOperator(string email, string displayName, string passwordHash, string totpSecret)
    {
        Email = email.Trim();
        NormalizedEmail = Email.ToUpperInvariant();
        DisplayName = displayName;
        PasswordHash = passwordHash;
        TotpSecret = totpSecret;
        IsActive = true;
    }

    public string Email { get; private set; } = "";

    public string NormalizedEmail { get; private set; } = "";

    public string DisplayName { get; private set; } = "";

    public string PasswordHash { get; private set; } = "";

    /// <summary>The TOTP shared secret, protected with Data Protection (never stored in clear).</summary>
    public string TotpSecret { get; private set; } = "";

    public bool IsActive { get; private set; }

    public int FailedAttempts { get; private set; }

    public DateTime? LockedUntil { get; private set; }

    public void Deactivate() => IsActive = false;

    public void RecordFailure(DateTime utcNow)
    {
        FailedAttempts++;
        if (FailedAttempts >= 5)
            LockedUntil = utcNow.AddMinutes(Math.Min(24 * 60, Math.Pow(2, FailedAttempts - 5)));
    }

    public void RecordSuccess()
    {
        FailedAttempts = 0;
        LockedUntil = null;
    }
}

public enum SupportAccessStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Revoked = 3,
}

/// <summary>Time-limited support access to one tenant for one operator, with a restricted role and a reason. Visible to the tenant.</summary>
public sealed class SupportAccessGrant : BaseEntity
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(8);

    private SupportAccessGrant()
    {
    }

    public SupportAccessGrant(Guid tenantId, Guid operatorId, string reason, Role role, string moduleKey, DateTime expiresOn, bool autoApprove)
    {
        TenantId = tenantId;
        OperatorId = operatorId;
        Reason = reason;
        Role = role;
        Module = moduleKey;
        ExpiresOn = expiresOn;
        Status = autoApprove ? SupportAccessStatus.Approved : SupportAccessStatus.Pending;
    }

    public Guid TenantId { get; private set; }

    public Guid OperatorId { get; private set; }

    public string Reason { get; private set; } = "";

    /// <summary>The restricted role the operator gets, in <see cref="Module"/> only.</summary>
    public Role Role { get; private set; }

    public string Module { get; private set; } = "";

    public DateTime ExpiresOn { get; private set; }

    public SupportAccessStatus Status { get; private set; }

    public string? DecidedBy { get; private set; }

    public bool IsUsable(DateTime utcNow) => Status == SupportAccessStatus.Approved && utcNow < ExpiresOn;

    public void Approve(string by) => Decide(SupportAccessStatus.Approved, by);

    public void Deny(string by) => Decide(SupportAccessStatus.Denied, by);

    public void Revoke(string by) => Decide(SupportAccessStatus.Revoked, by);

    private void Decide(SupportAccessStatus status, string by)
    {
        Status = status;
        DecidedBy = by;
    }
}

/// <summary>An append-only record of lifecycle, entitlement, onboarding and support-access events across tenants.</summary>
public sealed class TenancyAuditEntry : IAppendOnly
{
    private TenancyAuditEntry()
    {
    }

    public TenancyAuditEntry(DateTime occurredOn, Guid? tenantId, string kind, string? actor, string? details)
    {
        OccurredOn = occurredOn;
        TenantId = tenantId;
        Kind = kind;
        Actor = actor;
        Details = details;
    }

    public long Id { get; private set; }

    public DateTime OccurredOn { get; private set; }

    public Guid? TenantId { get; private set; }

    public string Kind { get; private set; } = "";

    public string? Actor { get; private set; }

    public string? Details { get; private set; }
}
