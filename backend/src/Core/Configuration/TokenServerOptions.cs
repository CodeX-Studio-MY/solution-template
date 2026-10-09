using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("TokenServer", "The token server in the Identity module: keys, lifetimes, platform clients and password rules. Keys come only from the secret store.")]
public sealed record TokenServerOptions
{
    [MinLength(1), Secret]
    [Setting("ES256 signing keys, PEM (EC P-256 private key). The first signs; the others stay published until their tokens expire. One entry per key.")]
    public List<string> SigningKeys { get; init; } = [];

    [MinLength(1), Secret]
    [Setting("Encryption keys for authorization codes and refresh tokens, base64 (256 bits). The first encrypts; the others still decrypt. One entry per key.")]
    public List<string> EncryptionKeys { get; init; } = [];

    [Secret]
    [Setting("Client secrets of platform clients, by client ID (the BFF and each service). Each service holds its own in ServiceClient:ClientSecret.", Example = "acme-bff")]
    public Dictionary<string, string> PlatformClientSecrets { get; init; } = [];

    [Range(1, 60)]
    [Setting("Access token lifetime for users and machines, in minutes.")]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 15)]
    [Setting("Access token lifetime for service principals, in minutes (5 to 15).")]
    public int ServiceAccessTokenMinutes { get; init; } = 10;

    [Range(1, 30)]
    [Setting("Refresh token lifetime, in days (sliding: renewed and rotated on each use).")]
    public int RefreshTokenDays { get; init; } = 7;

    [Range(1, 168)]
    [Setting("How long an emailed invitation link is valid, in hours.")]
    public int InvitationHours { get; init; } = 72;

    [Setting("Require HTTPS on the token server's endpoints. Turn off only when it sits on a private network behind the Gateway, which terminates TLS (the AppHost and compose do).")]
    public bool RequireTransportSecurity { get; init; } = true;

    [Setting("Reject passwords found in the Have I Been Pwned breached-password list (k-anonymity API).")]
    public bool BreachedPasswordCheck { get; init; } = true;
}

[OptionsSection("ServiceClient", "This service's own client credentials, for system work and for exchanging users' tokens when calling other services.")]
public sealed record ServiceClientOptions
{
    [Secret]
    [Setting("This service's client secret (from the secret store). Its client ID is derived from its service key.")]
    public string? ClientSecret { get; init; }

    [Url]
    [Setting("This process's own address, used for calls to services the same host runs (AllInOne.Host). Empty: http://localhost:{port of the first hosted service}.", Example = "http://localhost:5001")]
    public string? LocalAddress { get; init; }
}
