using Farm360.Core.Platform;

namespace Farm360.Identity.Application;

/// <summary>A link emailed to a person: where it goes, who it is for, and why.</summary>
public sealed record AccountLink(string Kind, string Email, string DisplayName, Uri Url, DateTime ExpiresOn);

/// <summary>Sends account emails. M3 replaces the default (which only logs) with an event for the Notification module.</summary>
public interface IAccountMessages
{
    Task SendAsync(AccountLink link, CancellationToken cancellationToken = default);
}

public sealed record InvitationResult(Guid PrincipalId, Uri Url, DateTime ExpiresOn);

/// <summary>Result of accepting an invitation or resetting a password.</summary>
public sealed record AccountOutcome(bool Succeeded, IReadOnlyList<string> Errors, Guid? PrincipalId = null)
{
    public static AccountOutcome Ok(Guid principalId) => new(true, [], principalId);

    public static AccountOutcome Fail(params string[] errors) => new(false, errors);
}

/// <summary>Invitations, password reset and first-sign-in setup for users in the current tenant.</summary>
public interface IAccountService
{
    /// <summary>
    /// Invites a person by email. Creates their (inactive) user principal, optionally with a first module role
    /// (the first admin is created this way during onboarding, never with a default password), and emails a single-use link.
    /// </summary>
    Task<InvitationResult> InviteAsync(string email, string displayName, IEnumerable<(string Module, Role Role)> roles, CancellationToken cancellationToken = default);

    /// <summary>Checks an invitation token without using it, for showing the set-password page.</summary>
    Task<bool> IsInvitationValidAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Accepts an invitation: creates the sign-in account with the chosen password and activates the principal.</summary>
    Task<AccountOutcome> AcceptInvitationAsync(string token, string password, CancellationToken cancellationToken = default);

    /// <summary>Emails a single-use reset link if the account exists. Always returns normally, so accounts can't be enumerated.</summary>
    Task RequestPasswordResetAsync(string userNameOrEmail, CancellationToken cancellationToken = default);

    Task<AccountOutcome> ResetPasswordAsync(string userId, string token, string password, CancellationToken cancellationToken = default);
}
