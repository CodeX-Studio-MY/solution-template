using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("Tenancy", "How requests are mapped to tenants.")]
public sealed record TenancyOptions
{
    [Required, RegularExpression(@"^[a-z0-9.-]+$")]
    [Setting("Base domain: tenants are served at {tenant}.{BaseDomain}, sign-in at login.{BaseDomain}.", Example = "localhost")]
    public string BaseDomain { get; init; } = "";
}
