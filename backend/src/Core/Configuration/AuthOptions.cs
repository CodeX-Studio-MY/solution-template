using System.ComponentModel.DataAnnotations;

namespace Farm360.Core.Configuration;

[OptionsSection("Auth", "How every service validates access tokens. The issuer is https://login.{Tenancy:BaseDomain}.")]
public sealed record AuthOptions
{
    [Url]
    [Setting("Internal URL of the token server's discovery document, when services can't reach the public issuer.", Example = "http://identity/.well-known/openid-configuration")]
    public string? MetadataAddress { get; init; }

    [Url]
    [Setting("Internal URL of the token endpoint used for service tokens and token exchange, when it differs from {issuer}connect/token.", Example = "http://identity/connect/token")]
    public string? TokenEndpoint { get; init; }

    [Setting("Require HTTPS for the discovery document. Only turn off for an internal HTTP address on a private network.")]
    public bool RequireHttpsMetadata { get; init; } = true;

    [Range(0, 30)]
    [Setting("Allowed clock skew for exp and nbf, in seconds (at most 30).")]
    public int ClockSkewSeconds { get; init; } = 30;

    [Range(0, 60)]
    [Setting("How long a principal's access_version from Redis is cached in memory, in seconds.")]
    public int AccessVersionCacheSeconds { get; init; } = 3;

    [Range(0, 3600)]
    [Setting("If Redis is unavailable, how long the last known access_version is used before failing closed (503), in seconds.")]
    public int AccessVersionGraceSeconds { get; init; } = 60;
}
