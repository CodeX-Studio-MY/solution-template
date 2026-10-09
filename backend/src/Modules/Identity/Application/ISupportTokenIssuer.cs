using Acme.Core.Platform;

namespace Acme.Identity.Application;

/// <summary>What an approved support-access grant allows: one restricted role in one module, for a short time.</summary>
public sealed record SupportTokenRequest(Guid GrantId, string OperatorId, string OperatorName, string Module, Role Role, string Audience, string Reason);

public sealed record SupportTokenResult(string AccessToken, DateTime ExpiresOn);

/// <summary>
/// Issues support-access tokens for platform operators. The token carries the tenant's <c>tenant_id</c>, a restricted
/// <c>roles</c> claim and an <c>act</c> claim identifying the operator, so every action is attributed to the operator.
/// Tenancy decides whether a grant is usable; Identity signs the token and records it in the tenant's audit log.
/// </summary>
public interface ISupportTokenIssuer
{
    Task<SupportTokenResult> IssueAsync(SupportTokenRequest request, CancellationToken cancellationToken = default);
}
