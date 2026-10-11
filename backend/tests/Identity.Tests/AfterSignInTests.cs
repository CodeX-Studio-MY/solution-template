using Acme.Core.Configuration;
using Acme.Identity.Api;

namespace Acme.Identity.Tests;

/// <summary>
/// Where a person goes once they have signed in on the central sign-in host. Accepting an invitation or resetting a password
/// arrives at the sign-in page without a return path; they must end up in their tenant's app, never on the sign-in host's
/// empty root, and never on another site.
/// </summary>
public sealed class AfterSignInTests
{
    private static readonly TenancyOptions Local = new() { BaseDomain = "localhost", PublicPort = 7443 };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void Without_a_return_path_the_person_lands_in_their_tenants_app(string? returnUrl) =>
        AccountEndpoints.AfterSignIn(returnUrl, "demo", Local).ShouldBe("https://demo.localhost:7443/");

    [Fact]
    public void The_authorize_request_the_gateway_started_is_continued_on_the_sign_in_host()
    {
        const string authorize = "/connect/authorize?client_id=bff&state=abc";
        AccountEndpoints.AfterSignIn(authorize, "demo", Local).ShouldBe(authorize);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example/")]
    [InlineData("javascript:alert(1)")]
    public void Another_site_is_never_followed(string returnUrl) =>
        AccountEndpoints.AfterSignIn(returnUrl, "demo", Local).ShouldBe("https://demo.localhost:7443/");

    [Fact]
    public void The_default_https_port_is_not_written()
    {
        var production = new TenancyOptions { BaseDomain = "example.com" };
        AccountEndpoints.AfterSignIn(null, "acme-co", production).ShouldBe("https://acme-co.example.com/");
    }
}
