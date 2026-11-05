using Saaskit.Tenancy.Domain;

namespace Saaskit.Tenancy.Application;

/// <summary>
/// One set of provider settings as the operator console shows it. The secret is never here: <see cref="HasSecret"/> says whether one is stored.
/// <see cref="CallbackUrl"/> is the address to give the provider.
/// </summary>
public sealed record IdentityProviderRow(
    short IdentityProviderId,
    LoginProvider ProviderType,
    string ProviderKey,
    string DisplayName,
    bool IsEnabled,
    string? ClientId,
    bool HasSecret,
    string? Authority,
    string? AppleTeamId,
    string? AppleKeyId,
    string CallbackUrl,
    int LinkedAccounts,
    string ETag);

/// <summary>What creates a set of settings. The kind and key never change afterwards. New settings start disabled.</summary>
public sealed record IdentityProviderCreate(
    LoginProvider ProviderType,
    string ProviderKey,
    string DisplayName,
    string? ClientId = null,
    string? ClientSecret = null,
    string? Authority = null,
    string? AppleTeamId = null,
    string? AppleKeyId = null);

/// <summary>What replaces the settings. A blank <paramref name="ClientSecret"/> keeps the stored one.</summary>
public sealed record IdentityProviderUpdate(
    string DisplayName,
    string? ClientId = null,
    string? ClientSecret = null,
    string? Authority = null,
    string? AppleTeamId = null,
    string? AppleKeyId = null);

/// <summary>
/// The settings for signing in with Google, Microsoft, Apple and Facebook. Sign-in with them is not connected yet:
/// this holds what the connection will need. Secrets are encrypted and never returned or logged.
/// </summary>
public interface IIdentityProviderCatalog
{
    Task<IReadOnlyList<IdentityProviderRow>> ListAsync(CancellationToken cancellationToken = default);

    Task<IdentityProviderRow> CreateAsync(IdentityProviderCreate input, Guid actor, CancellationToken cancellationToken = default);

    Task<IdentityProviderRow> UpdateAsync(short id, IdentityProviderUpdate input, string? ifMatch, Guid actor, CancellationToken cancellationToken = default);

    /// <summary>Enabling needs the client ID and a stored secret, plus the authority (Microsoft) or the team and key IDs (Apple).</summary>
    Task<IdentityProviderRow> SetEnabledAsync(short id, bool enabled, string? ifMatch, Guid actor, CancellationToken cancellationToken = default);

    /// <summary>Settings with linked accounts can't be deleted, only disabled.</summary>
    Task DeleteAsync(short id, Guid actor, CancellationToken cancellationToken = default);
}
