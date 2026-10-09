using Acme.Core.Configuration;
using Acme.Core.Errors;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.Core.Tenancy;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Acme.Identity.Infrastructure;

internal sealed class SupportTokenIssuer(
    ITenantContext tenant,
    IOptions<TokenServerOptions> tokenServer,
    IOptions<TenancyOptions> tenancy,
    IAuditWriter audit,
    TimeProvider time) : ISupportTokenIssuer
{
    /// <summary>Support tokens are short: the operator console asks again, and Tenancy re-checks the grant each time.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<SupportTokenResult> IssueAsync(SupportTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (!TokenAudiences.Known.Contains(request.Audience))
            throw new BusinessRuleException(IdentityErrors.UnknownModule, "The audience must be a platform service.");

        var now = time.GetUtcNow().UtcDateTime;
        var expires = now.Add(Lifetime);
        var key = TokenServerKeys.SigningKeys(tokenServer.Value)[0];
        var claims = new Dictionary<string, object>
        {
            [PlatformClaims.Subject] = $"support-{request.GrantId:N}",
            [PlatformClaims.PrincipalType] = PrincipalType.User.ToClaimValue(),
            [PlatformClaims.TenantId] = tenant.Tenant.Id.ToString(),
            [PlatformClaims.AccessVersion] = 0L,
            [PlatformClaims.Roles] = new[] { RoleExtensions.ToModuleRole(request.Module, request.Role) },
            [PlatformClaims.Actor] = new Dictionary<string, object> { [PlatformClaims.Subject] = request.OperatorId },
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = tenancy.Value.Issuer,
            Audience = request.Audience,
            Claims = claims,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            TokenType = "at+jwt",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256),
        });

        // The tenant sees this in its own audit log: who (the operator), what access, why, and until when.
        await audit.WriteAsync(
            AuditKinds.SupportAccess,
            subject: request.GrantId.ToString(),
            details: new { request.Module, role = request.Role.ToClaimValue(), request.Audience, request.Reason, expires },
            actorId: request.OperatorId,
            actorType: "operator",
            cancellationToken: cancellationToken);
        return new SupportTokenResult(token, expires);
    }
}
