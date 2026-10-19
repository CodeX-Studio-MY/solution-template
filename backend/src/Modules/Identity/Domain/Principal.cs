using Saaskit.Core.Data;
using Saaskit.Core.Security;

namespace Saaskit.Identity.Domain;

/// <summary>
/// Anything that can sign in: a user, a machine or a service. Everything is authorized the same way, by its
/// effective role in each module. A user's sign-in details live in the user store under the same ID.
/// </summary>
public sealed class Principal : BaseEntity, IAggregateRoot
{
    public const int MaxDisplayNameLength = 200;

    private Principal()
    {
    }

    private Principal(PrincipalType type, string displayName)
    {
        Type = type;
        DisplayName = displayName;
        IsActive = true;
        AccessVersion = 1;
    }

    public PrincipalType Type { get; private set; }

    public string DisplayName { get; private set; } = "";

    public bool IsActive { get; private set; }

    /// <summary>Raised whenever the principal's effective access may have changed; tokens carrying an older value are refused.</summary>
    public long AccessVersion { get; private set; }

    /// <summary>The OAuth client ID of a machine or service principal.</summary>
    public string? ClientId { get; private set; }

    public DateTime? LastSeenOn { get; private set; }

    public static Principal NewUser(string displayName) => new(PrincipalType.User, displayName);

    public static Principal NewMachine(string displayName) => new(PrincipalType.Machine, displayName);

    public static Principal NewService(string displayName) => new(PrincipalType.Service, displayName);

    public void AttachClient(string clientId) => ClientId = clientId;

    public void Rename(string displayName) => DisplayName = displayName;

    public void Disable()
    {
        IsActive = false;
        BumpAccessVersion();
    }

    public void Enable()
    {
        IsActive = true;
        BumpAccessVersion();
    }

    public void BumpAccessVersion() => AccessVersion++;

    public void Seen(DateTime utcNow) => LastSeenOn = utcNow;
}
