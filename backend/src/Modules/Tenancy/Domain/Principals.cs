using Saaskit.Core.Data;

namespace Saaskit.Tenancy.Domain;

// Stored as tinyint: the values are part of the schema, so never renumber them.
public enum PlatformPrincipalType : byte
{
    User = 1,
    Device = 2,
    Application = 3,
}

public enum DeviceType : byte
{
    Iot = 1,
    LorawanGateway = 2,
    Gateway = 3,
}

/// <summary>The kind of an identity provider (Google, Facebook, ...). One kind can have several sets of settings.</summary>
public enum LoginProvider : byte
{
    Google = 1,
    Microsoft = 2,
    Apple = 3,
    Facebook = 4,
}

public enum TenantAccessStatus : byte
{
    Active = 1,
    Suspended = 2,
    Revoked = 3,
}

public enum TenantAccessAction : byte
{
    Granted = 1,
    Suspended = 2,
    Resumed = 3,
    Revoked = 4,
}

public enum LoginMethod : byte
{
    Password = 1,
    Passkey = 2,
    ClientCredentials = 3,
    Google = 4,
    Microsoft = 5,
    Apple = 6,
    Facebook = 7,
}

public enum LoginFailureReason : byte
{
    WrongCredentials = 1,
    LockedOut = 2,
    UnknownAccount = 3,
    Deactivated = 4,
    NoTenantAccess = 5,
    NoLinkedAccount = 6,
}

/// <summary>
/// A user, device or application in the catalog, which can be given access to many tenants. Not yet connected to sign-in:
/// tokens and roles still come from Identity's <c>Principal</c> in each tenant database.
/// </summary>
public sealed class PlatformPrincipal : BaseEntity, IAggregateRoot
{
    public const int MaxDisplayNameLength = 200;

    private PlatformPrincipal()
    {
    }

    public PlatformPrincipal(PlatformPrincipalType type, string displayName)
    {
        Type = type;
        DisplayName = displayName;
        IsActive = true;
    }

    public PlatformPrincipalType Type { get; private set; }

    public string DisplayName { get; private set; } = "";

    public bool IsActive { get; private set; }

    public void Rename(string displayName) => DisplayName = displayName;

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>A user's details. The key is the principal's ID.</summary>
public sealed class PrincipalUser
{
    public const int MaxEmailLength = 254;

    private PrincipalUser()
    {
    }

    public PrincipalUser(Guid principalId, string email)
    {
        PrincipalUserId = principalId;
        Email = email;
    }

    public Guid PrincipalUserId { get; private set; }

    /// <summary>Lowercase, so an exact match can use the unique index.</summary>
    public string Email { get; private set; } = "";

    /// <summary>When the address was confirmed (by a link or a provider that vouches for it). Empty means not confirmed.</summary>
    public DateTime? EmailVerifiedOn { get; private set; }

    public void Change(string email) => Email = email;
}

/// <summary>
/// An outside account linked to a user. Matched by the provider settings used and the subject, never by email.
/// A user can link any number of accounts, across kinds of provider and across settings rows.
/// </summary>
public sealed class PrincipalUserProvider
{
    private PrincipalUserProvider()
    {
    }

    public PrincipalUserProvider(Guid principalUserId, short identityProviderId, string providerSubject, string? providerEmail, bool providerEmailVerified, DateTime linkedOn)
    {
        PrincipalUserId = principalUserId;
        IdentityProviderId = identityProviderId;
        ProviderSubject = providerSubject;
        ProviderEmail = providerEmail;
        ProviderEmailVerified = providerEmailVerified;
        LinkedOn = linkedOn;
    }

    public long PrincipalUserProviderId { get; private set; }

    public Guid PrincipalUserId { get; private set; }

    /// <summary>The settings row this account signs in through.</summary>
    public short IdentityProviderId { get; private set; }

    /// <summary>The provider's permanent ID. Some providers issue a different one per app, which is why it is unique with the settings row.</summary>
    public string ProviderSubject { get; private set; } = "";

    /// <summary>For display only. Facebook may not supply one.</summary>
    public string? ProviderEmail { get; private set; }

    /// <summary>Whether the provider vouches for the email: the account-linking rule needs it.</summary>
    public bool ProviderEmailVerified { get; private set; }

    public DateTime LinkedOn { get; private set; }

    public DateTime? LastUsedOn { get; private set; }

    public void RecordUse(DateTime utcNow) => LastUsedOn = utcNow;
}

/// <summary>A device's details. The key is the principal's ID.</summary>
public sealed class PrincipalDevice
{
    public const int MaxIdentifierLength = 64;
    public const int MaxMakerLength = 100;

    private PrincipalDevice()
    {
    }

    public PrincipalDevice(Guid principalId, DeviceType deviceType, string identifier, string? manufacturer, string? model)
    {
        PrincipalDeviceId = principalId;
        Change(deviceType, identifier, manufacturer, model);
    }

    public Guid PrincipalDeviceId { get; private set; }

    public DeviceType DeviceType { get; private set; }

    /// <summary>A serial number or EUI, stored uppercase.</summary>
    public string Identifier { get; private set; } = "";

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    public void Change(DeviceType deviceType, string identifier, string? manufacturer, string? model)
    {
        DeviceType = deviceType;
        Identifier = identifier;
        Manufacturer = manufacturer;
        Model = model;
    }
}

/// <summary>An application's details. The key is the principal's ID.</summary>
public sealed class PrincipalApplication
{
    public const int MaxOwnerNameLength = 200;

    private PrincipalApplication()
    {
    }

    public PrincipalApplication(Guid principalId, bool isExternal, string ownerName, string? contactEmail)
    {
        PrincipalApplicationId = principalId;
        Change(isExternal, ownerName, contactEmail);
    }

    public Guid PrincipalApplicationId { get; private set; }

    public bool IsExternal { get; private set; }

    public string OwnerName { get; private set; } = "";

    public string? ContactEmail { get; private set; }

    public void Change(bool isExternal, string ownerName, string? contactEmail)
    {
        IsExternal = isExternal;
        OwnerName = ownerName;
        ContactEmail = contactEmail;
    }
}

/// <summary>Whether a principal may access a tenant. One row per pair: granting again reactivates it.</summary>
public sealed class TenantPrincipalAccess : BaseEntity, IAggregateRoot
{
    private TenantPrincipalAccess()
    {
    }

    public TenantPrincipalAccess(Guid tenantId, Guid principalId, DateTime grantedOn, Guid grantedBy)
    {
        TenantId = tenantId;
        PrincipalId = principalId;
        Grant(grantedOn, grantedBy);
    }

    public Guid TenantId { get; private set; }

    public Guid PrincipalId { get; private set; }

    public TenantAccessStatus Status { get; private set; }

    public DateTime GrantedOn { get; private set; }

    public Guid GrantedBy { get; private set; }

    public void Grant(DateTime grantedOn, Guid grantedBy)
    {
        Status = TenantAccessStatus.Active;
        GrantedOn = grantedOn;
        GrantedBy = grantedBy;
    }

    /// <summary>What moving to <paramref name="to"/> is called, or null if that move isn't allowed. A revoked row only comes back through a new grant.</summary>
    public TenantAccessAction? ActionFor(TenantAccessStatus to) => (Status, to) switch
    {
        (TenantAccessStatus.Active, TenantAccessStatus.Suspended) => TenantAccessAction.Suspended,
        (TenantAccessStatus.Suspended, TenantAccessStatus.Active) => TenantAccessAction.Resumed,
        (TenantAccessStatus.Active or TenantAccessStatus.Suspended, TenantAccessStatus.Revoked) => TenantAccessAction.Revoked,
        _ => null,
    };

    public void MoveTo(TenantAccessStatus status) => Status = status;
}

/// <summary>An append-only record of one access change.</summary>
public sealed class TenantPrincipalAccessAudit : IAppendOnly
{
    public const int MaxReasonLength = 500;

    private TenantPrincipalAccessAudit()
    {
    }

    public TenantPrincipalAccessAudit(DateTime occurredOn, Guid tenantId, Guid principalId, TenantAccessAction action, TenantAccessStatus? fromStatus, TenantAccessStatus toStatus, Guid actorId, string? reason, string? remoteAddress, string? traceId)
    {
        OccurredOn = occurredOn;
        TenantId = tenantId;
        PrincipalId = principalId;
        Action = action;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ActorId = actorId;
        Reason = reason;
        RemoteAddress = remoteAddress;
        TraceId = traceId;
    }

    public long TenantPrincipalAccessAuditId { get; private set; }

    public DateTime OccurredOn { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid PrincipalId { get; private set; }

    public TenantAccessAction Action { get; private set; }

    public TenantAccessStatus? FromStatus { get; private set; }

    public TenantAccessStatus ToStatus { get; private set; }

    public Guid ActorId { get; private set; }

    public string? Reason { get; private set; }

    public string? RemoteAddress { get; private set; }

    public string? TraceId { get; private set; }
}

/// <summary>An append-only record of one login attempt. Holds a reference to the login session, never a token.</summary>
public sealed class TenantPrincipalLogin : IAppendOnly
{
    private TenantPrincipalLogin()
    {
    }

    public TenantPrincipalLogin(DateTime occurredOn, Guid tenantId, Guid? principalId, PlatformPrincipalType? principalType, bool succeeded, LoginMethod method, LoginFailureReason? failureReason, Guid? authorizationId, string? remoteAddress, string? userAgent, string? traceId)
    {
        OccurredOn = occurredOn;
        TenantId = tenantId;
        PrincipalId = principalId;
        PrincipalType = principalType;
        Succeeded = succeeded;
        Method = method;
        FailureReason = failureReason;
        AuthorizationId = authorizationId;
        RemoteAddress = remoteAddress;
        UserAgent = userAgent;
        TraceId = traceId;
    }

    public long TenantPrincipalLoginId { get; private set; }

    public DateTime OccurredOn { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid? PrincipalId { get; private set; }

    public PlatformPrincipalType? PrincipalType { get; private set; }

    public bool Succeeded { get; private set; }

    public LoginMethod Method { get; private set; }

    public LoginFailureReason? FailureReason { get; private set; }

    /// <summary>OpenIddict's authorization, which groups every rotated refresh token of this login.</summary>
    public Guid? AuthorizationId { get; private set; }

    public string? RemoteAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? TraceId { get; private set; }
}
