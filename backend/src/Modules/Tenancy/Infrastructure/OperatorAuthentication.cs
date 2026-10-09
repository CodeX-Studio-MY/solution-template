using System.Security.Claims;
using System.Text.Encodings.Web;
using Acme.Tenancy.Application;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Tenancy.Infrastructure;

/// <summary>
/// Authenticates platform operators on the operator console: <c>Authorization: Operator {session token}</c>. A separate
/// scheme from tenant bearer tokens, because operators are a separate realm.
/// </summary>
internal sealed class OperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOperatorSessions sessions) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Operator";
    public const string Policy = "Operator";
    public const string OperatorIdClaim = "operator_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(SchemeName + " ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var identity = await sessions.ValidateAsync(header[(SchemeName.Length + 1)..], Context.RequestAborted);
        if (identity is null)
            return AuthenticateResult.Fail("The operator session is not valid.");

        var claims = new[] { new Claim(OperatorIdClaim, identity.Id.ToString()), new Claim(ClaimTypes.Name, identity.DisplayName), new Claim(ClaimTypes.Email, identity.Email) };
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName));
    }
}
