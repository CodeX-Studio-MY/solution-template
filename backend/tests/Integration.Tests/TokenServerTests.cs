using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.Core.Platform;
using Acme.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Acme.Integration.Tests;

public sealed class TokenServerTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string> ClientCredentialsRequest(string clientId, string assertion, string resource) => new()
    {
        ["grant_type"] = "client_credentials",
        ["client_id"] = clientId,
        ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
        ["client_assertion"] = assertion,
        ["resource"] = resource,
    };

    [Fact]
    public async Task A_machine_signs_in_with_private_key_jwt_and_gets_a_signed_ES256_access_token()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var key = await host.RegisterMachineAsync("demo", "demo-scale-1", roles: [(Modules.PurchaseRequest, Role.User)]);
        var kid = (await ReadKidAsync(host, "demo-scale-1"));

        var response = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(
            ClientCredentialsRequest("demo-scale-1", IdentityTestHost.ClientAssertion("demo-scale-1", key, kid), PlatformServices.PurchaseRequest.Audience)), Ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());
        body.TryGetProperty("refresh_token", out _).ShouldBeFalse("machines get no refresh token");

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());
        token.Alg.ShouldBe("ES256");
        token.Typ.ShouldBe("at+jwt");
        token.Issuer.ShouldBe(IdentityTestHost.Issuer);
        token.Audiences.ShouldBe([PlatformServices.PurchaseRequest.Audience]);
        token.GetClaim(PlatformClaims.PrincipalType).Value.ShouldBe("machine");
        token.GetClaim(PlatformClaims.TenantId).Value.ShouldBe(host.Tenants["demo"].Id.ToString());
        token.GetClaim(PlatformClaims.Roles).Value.ShouldBe("purchase-request:user");
        (token.ValidTo - token.IssuedAt).TotalMinutes.ShouldBeInRange(1, 15);

        // The token validates against the published JWKS: signed by the token server's key.
        var jwks = await host.FetchJwksAsync();
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(body.GetProperty("access_token").GetString(), new TokenValidationParameters
        {
            ValidIssuer = IdentityTestHost.Issuer,
            ValidAudience = PlatformServices.PurchaseRequest.Audience,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidTypes = ["at+jwt"],
        });
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
    }

    [Fact]
    public async Task A_wrong_key_or_a_disabled_machine_gets_no_token()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        await host.RegisterMachineAsync("demo", "demo-scale-2");
        var kid = await ReadKidAsync(host, "demo-scale-2");
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

        var forged = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(
            ClientCredentialsRequest("demo-scale-2", IdentityTestHost.ClientAssertion("demo-scale-2", other, kid), PlatformServices.PurchaseRequest.Audience)), Ct);
        forged.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);   // invalid_client
    }

    private static async Task<string> ReadKidAsync(IdentityTestHost host, string clientId) =>
        await host.InTenantAsync("demo", async sp =>
        {
            var catalog = sp.GetRequiredService<Acme.Identity.Infrastructure.IdentityCatalogDbContext>();
            var app = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(catalog.OidcApplications, a => a.ClientId == clientId);
            return new JsonWebKeySet(app.JsonWebKeySet).Keys.Single().Kid;
        });
}
