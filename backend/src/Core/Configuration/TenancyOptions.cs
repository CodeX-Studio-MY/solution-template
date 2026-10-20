using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Farm360.Core.Configuration;

[OptionsSection("Tenancy", "How requests are mapped to tenants, and the public origins derived from the base domain.")]
public sealed record TenancyOptions
{
    [Required, RegularExpression(@"^[a-z0-9.-]+$")]
    [Setting("Base domain: tenants are served at {tenant}.{BaseDomain}, sign-in at login.{BaseDomain}.", Example = "localhost")]
    public string BaseDomain { get; init; } = "";

    [Range(1, 65535)]
    [Setting("Public HTTPS port of the Gateway, when it isn't 443 (local development).", Example = "7443")]
    public int? PublicPort { get; init; }

    /// <summary>
    /// The token server's issuer: the single, constant <c>iss</c> for every tenant. It ends with a slash, exactly as OpenIddict
    /// issues it, so services compare the same string everywhere.
    /// </summary>
    public string Issuer => Origin("login") + "/";

    /// <summary>The public origin of a tenant's app, BFF and API, for example <c>https://demo.example.com</c>.</summary>
    public string TenantOrigin(string slug) => Origin(slug);

    private string Origin(string label) =>
        PublicPort is null or 443
            ? $"https://{label}.{BaseDomain}"
            : string.Create(CultureInfo.InvariantCulture, $"https://{label}.{BaseDomain}:{PublicPort}");
}
