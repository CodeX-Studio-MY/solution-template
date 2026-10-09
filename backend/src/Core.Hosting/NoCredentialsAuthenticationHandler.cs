using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Acme.Core.Hosting;

/// <summary>
/// The default scheme until a host adds token validation: it authenticates nobody, so the fallback policy
/// answers 401 instead of failing. Token validation (ES256 JWTs) replaces it as the default scheme.
/// </summary>
internal sealed class NoCredentialsAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "NoCredentials";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
