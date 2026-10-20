using Farm360.Core.Data;

namespace Farm360.Identity.Domain;

/// <summary>An emailed, single-use invitation to create a user. Only a hash of the token is stored.</summary>
public sealed class Invitation : BaseEntity
{
    private Invitation()
    {
    }

    public Invitation(string email, string tokenHash, DateTime expiresOn, Guid principalId)
    {
        Email = email;
        NormalizedEmail = email.ToUpperInvariant();
        TokenHash = tokenHash;
        ExpiresOn = expiresOn;
        PrincipalId = principalId;
    }

    public string Email { get; private set; } = "";

    public string NormalizedEmail { get; private set; } = "";

    public string TokenHash { get; private set; } = "";

    public DateTime ExpiresOn { get; private set; }

    public DateTime? AcceptedOn { get; private set; }

    /// <summary>The user principal created for the invitee (inactive until accepted).</summary>
    public Guid PrincipalId { get; private set; }

    public bool CanAccept(DateTime utcNow) => AcceptedOn is null && utcNow < ExpiresOn;

    public void Accept(DateTime utcNow) => AcceptedOn = utcNow;
}

/// <summary>A one-time machine enrollment code (valid 15 minutes, single use). Only a hash is stored.</summary>
public sealed class MachineEnrollment : BaseEntity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private MachineEnrollment()
    {
    }

    public MachineEnrollment(Guid principalId, string codeHash, DateTime expiresOn)
    {
        PrincipalId = principalId;
        CodeHash = codeHash;
        ExpiresOn = expiresOn;
    }

    public Guid PrincipalId { get; private set; }

    public string CodeHash { get; private set; } = "";

    public DateTime ExpiresOn { get; private set; }

    public DateTime? UsedOn { get; private set; }

    public bool CanUse(DateTime utcNow) => UsedOn is null && utcNow < ExpiresOn;

    public void Use(DateTime utcNow) => UsedOn = utcNow;
}
