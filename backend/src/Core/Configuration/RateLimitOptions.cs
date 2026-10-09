using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("RateLimit", "Distributed rate limits (Redis). A tenant's plan can override the per-tenant and per-principal limits.")]
public sealed record RateLimitOptions
{
    [Range(1, 1_000_000)]
    [Setting("Requests per minute for one tenant, across all instances, unless the plan sets its own.")]
    public int PerTenantPerMinute { get; init; } = 1200;

    [Range(1, 1_000_000)]
    [Setting("Requests per minute for one principal, across all instances, unless the plan sets its own.")]
    public int PerPrincipalPerMinute { get; init; } = 300;

    [Range(1, 10_000)]
    [Setting("Requests per minute per client address for sign-in, token, enrollment and password-reset endpoints.")]
    public int AuthPerAddressPerMinute { get; init; } = 30;

    [Range(1, 1_000_000)]
    [Setting("Coarse per-instance protection: requests per second this instance accepts in total.")]
    public int PerInstancePerSecond { get; init; } = 1000;
}
