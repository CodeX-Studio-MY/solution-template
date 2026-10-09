using Acme.Core.Platform;
using Acme.Tenancy.Domain;

namespace Acme.Tenancy.Application;

/// <summary>A newly created operator: shown once. The password and TOTP secret are never stored in clear.</summary>
public sealed record NewOperator(Guid Id, string Email, string Password, string TotpSecret, string TotpUri);

public sealed record OperatorIdentity(Guid Id, string Email, string DisplayName);

public sealed record SessionDto(string Token, DateTime ExpiresOn);

/// <summary>Creates platform operators. Run from the Migrator tool (bootstrap) or by an existing operator.</summary>
public interface IOperatorAccounts
{
    Task<NewOperator> CreateAsync(string email, string displayName, CancellationToken cancellationToken = default);
}

/// <summary>Operator sign-in. MFA (a TOTP code) is always required; failed attempts lock the account with growing delays.</summary>
public interface IOperatorSessions
{
    Task<SessionDto> SignInAsync(string email, string password, string totpCode, CancellationToken cancellationToken = default);

    /// <summary>Validates an operator session token and returns who it is for, or null.</summary>
    Task<OperatorIdentity?> ValidateAsync(string token, CancellationToken cancellationToken = default);
}

public sealed record SupportAccessDto(Guid Id, Guid TenantId, string TenantSlug, Guid OperatorId, string OperatorName, string Reason, Role Role, string Module, DateTime ExpiresOn, SupportAccessStatus Status);

public sealed record SupportAccessRequest(string TenantSlug, string Reason, Role Role, string Module, int Minutes);

/// <summary>
/// Time-limited, audited support access. A tenant admin can see every grant and may require approval (on by default);
/// the operator's token carries a restricted role and identifies the operator, so every action is attributed to them.
/// </summary>
public interface ISupportAccess
{
    Task<SupportAccessDto> RequestAsync(OperatorIdentity operatorIdentity, SupportAccessRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupportAccessDto>> ListForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<SupportAccessDto> DecideAsync(Guid tenantId, Guid grantId, SupportAccessStatus decision, string decidedBy, CancellationToken cancellationToken = default);

    /// <summary>Issues a short-lived token for an approved, unexpired grant. Re-checked on every call, so revoking takes effect at the next renewal.</summary>
    Task<SupportTokenDto> IssueTokenAsync(OperatorIdentity operatorIdentity, Guid grantId, CancellationToken cancellationToken = default);
}
