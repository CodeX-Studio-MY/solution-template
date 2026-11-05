using Saaskit.Core.Data;

namespace Saaskit.Tenancy.Domain;

/// <summary>
/// One set of settings for signing in with an outside provider. A kind of provider can have several rows (two Microsoft
/// directories, separate Google apps). One disabled row per kind is seeded; operators add more. Sign-in with these is not
/// connected yet: this holds what the connection will need.
/// </summary>
public sealed class IdentityProvider : AuditedEntity, IAggregateRoot
{
    public const int MaxDisplayNameLength = 50;
    public const int MaxClientIdLength = 255;
    public const int MaxAuthorityLength = 64;
    public const int AppleIdLength = 10;
    public const int MaxSecretLength = 2000;
    public const int MaxProtectedSecretLength = 4000;

    private IdentityProvider()
    {
    }

    public IdentityProvider(LoginProvider providerType, string providerKey, string displayName)
    {
        ProviderType = providerType;
        ProviderKey = providerKey;
        DisplayName = displayName;
    }

    public short IdentityProviderId { get; private set; }

    public LoginProvider ProviderType { get; private set; }

    /// <summary>Never changes: it is part of the callback URL given to the provider.</summary>
    public string ProviderKey { get; private set; } = "";

    /// <summary>The label on the login button.</summary>
    public string DisplayName { get; private set; } = "";

    public bool IsEnabled { get; private set; }

    /// <summary>The app ID on Facebook, the Services ID on Apple.</summary>
    public string? ClientId { get; private set; }

    /// <summary>Encrypted with Data Protection. Apple's private key goes here. Never returned by the API.</summary>
    public string? ClientSecretProtected { get; private set; }

    /// <summary>Microsoft only: <c>common</c>, <c>organizations</c>, <c>consumers</c> or a directory ID.</summary>
    public string? Authority { get; private set; }

    public string? AppleTeamId { get; private set; }

    public string? AppleKeyId { get; private set; }

    /// <summary>Whether everything this kind of provider needs is filled in.</summary>
    public bool IsComplete => MissingSettings().Count == 0;

    /// <summary>The settings still missing before this can be enabled, by field name.</summary>
    public IReadOnlyList<string> MissingSettings()
    {
        var missing = new List<string>();
        if (string.IsNullOrEmpty(ClientId))
            missing.Add("clientId");
        if (string.IsNullOrEmpty(ClientSecretProtected))
            missing.Add("clientSecret");
        if (ProviderType == LoginProvider.Microsoft && string.IsNullOrEmpty(Authority))
            missing.Add("authority");
        if (ProviderType == LoginProvider.Apple)
        {
            if (string.IsNullOrEmpty(AppleTeamId))
                missing.Add("appleTeamId");
            if (string.IsNullOrEmpty(AppleKeyId))
                missing.Add("appleKeyId");
        }
        return missing;
    }

    /// <summary>Replaces the settings. Fields that don't belong to the kind are cleared. A null <paramref name="protectedSecret"/> keeps the current secret.</summary>
    public void Change(string displayName, string? clientId, string? protectedSecret, string? authority, string? appleTeamId, string? appleKeyId)
    {
        DisplayName = displayName;
        ClientId = clientId;
        if (protectedSecret is not null)
            ClientSecretProtected = protectedSecret;
        Authority = ProviderType == LoginProvider.Microsoft ? authority : null;
        AppleTeamId = ProviderType == LoginProvider.Apple ? appleTeamId : null;
        AppleKeyId = ProviderType == LoginProvider.Apple ? appleKeyId : null;
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
