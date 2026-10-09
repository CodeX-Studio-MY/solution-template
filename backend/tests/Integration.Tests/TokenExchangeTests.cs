using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.Core.Platform;
using Acme.Identity.Application;
using Acme.Identity.Domain;
using Acme.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Acme.Integration.Tests;

/// <summary>RFC 8693 token exchange: audience exchange between modules, and a user delegated onto a machine.</summary>
public sealed class TokenExchangeTests(Databases databases)
{
    private const string ExchangeGrant = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";
    private const string ServiceClient = "acme-purchase-request";
    private const string ServiceSecret = "service-test-secret";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(IdentityTestHost Host, string UserToken, Guid UserId)> StartWithUserAsync(Databases databases)
    {
        var host = await UserFlowTests.StartWithBffAsync(databases);
        await host.InTenantAsync("demo", sp => sp.GetRequiredService<IClientRegistry>()
            .RegisterAsync(ServiceClient, "purchase-request", null, new ClientCredential(Secret: ServiceSecret), [ExchangeGrant], Ct));
        var email = await UserFlowTests.CreateUserAsync(host, "tan@example.com", Role.Manager);

        var (verifier, challenge) = LoginBrowser.Pkce();
        using var browser = host.Browser();
        var code = await UserFlowTests.SignInForCodeAsync(host, email, challenge, browser);
        var response = await UserFlowTests.TokenAsync(host, new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = UserFlowTests.Callback, ["code_verifier"] = verifier,
        });
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString()!;
        var userId = await host.InTenantAsync("demo", async sp => (await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.DisplayName == email, Ct)).Id);
        return (host, token, userId);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(IdentityTestHost host, string subjectToken, string audience, string? actorToken = null, string client = ServiceClient, string secret = ServiceSecret)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = ExchangeGrant, ["subject_token"] = subjectToken, ["subject_token_type"] = AccessTokenType,
            ["resource"] = audience, ["tenant"] = "demo", ["client_id"] = client, ["client_secret"] = secret,
        };
        if (actorToken is not null)
        {
            fields["actor_token"] = actorToken;
            fields["actor_token_type"] = AccessTokenType;
        }
        return host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(fields), Ct);
    }

    [Fact]
    public async Task A_module_exchanges_the_users_token_for_another_modules_audience_and_the_user_is_preserved()
    {
        var (host, userToken, userId) = await StartWithUserAsync(databases);
        await using var hostScope = host;

        var response = await ExchangeAsync(host, userToken, PlatformServices.CostCenterBudget.Audience);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());
        token.Audiences.ShouldBe([PlatformServices.CostCenterBudget.Audience]);
        token.Subject.ShouldBe(userId.ToString());   // the other module still sees the user
        token.GetClaim("principal_type").Value.ShouldBe("user");
        token.GetClaim("roles").Value.ShouldBe("purchase-request:manager");
        token.TryGetClaim("act", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_user_on_a_shared_machine_gets_a_delegated_token_with_the_user_as_subject_and_the_machine_as_actor()
    {
        var (host, userToken, userId) = await StartWithUserAsync(databases);
        await using var hostScope = host;
        var machineKey = await host.RegisterMachineAsync("demo", "demo-tablet", roles: [(Modules.PurchaseRequest, Role.Admin)]);
        var kid = await host.InTenantAsync("demo", async sp =>
            new Microsoft.IdentityModel.Tokens.JsonWebKeySet((await sp.GetRequiredService<IdentityCatalogDbContext>().OidcApplications.SingleAsync(a => a.ClientId == "demo-tablet", Ct)).JsonWebKeySet).Keys.Single().Kid);
        var machineId = await host.InTenantAsync("demo", async sp => (await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.ClientId == "demo-tablet", Ct)).Id);

        var machineResponse = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "demo-tablet", ["resource"] = PlatformServices.Identity.Audience,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = IdentityTestHost.ClientAssertion("demo-tablet", machineKey, kid),
        }), Ct);
        var machineToken = (await machineResponse.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString()!;

        var response = await ExchangeAsync(host, userToken, PlatformServices.PurchaseRequest.Audience, actorToken: machineToken);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());

        var token = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());
        token.Subject.ShouldBe(userId.ToString());
        using var act = JsonDocument.Parse(token.GetClaim("act").Value);
        act.RootElement.GetProperty("sub").GetString().ShouldBe(machineId.ToString());
        // Authorized with the user's roles (manager), not the machine's (admin).
        token.GetClaim("roles").Value.ShouldBe("purchase-request:manager");

        // The audit log records both the user and the machine.
        await host.InTenantAsync("demo", async sp =>
        {
            var entry = await sp.GetRequiredService<IdentityDbContext>().AuditLog.SingleAsync(a => a.Kind == AuditKinds.DelegatedTokenIssued, Ct);
            entry.ActorId.ShouldBe(userId.ToString());
            entry.OnBehalfOf.ShouldBe(machineId.ToString());
        });
    }

    [Fact]
    public async Task Exchange_is_refused_for_a_forged_actor_a_wrong_client_an_unknown_audience_and_revoked_access()
    {
        var (host, userToken, userId) = await StartWithUserAsync(databases);
        await using var hostScope = host;

        // An actor token that is not a machine token we issued.
        (await ExchangeAsync(host, userToken, PlatformServices.PurchaseRequest.Audience, actorToken: userToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // A client without the token-exchange grant.
        (await ExchangeAsync(host, userToken, PlatformServices.PurchaseRequest.Audience, client: "acme-bff", secret: "bff-test-secret")).StatusCode.ShouldNotBe(HttpStatusCode.OK);
        // An audience that isn't a platform service.
        (await ExchangeAsync(host, userToken, "acme:something-else")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // Wrong client secret.
        (await ExchangeAsync(host, userToken, PlatformServices.PurchaseRequest.Audience, secret: "nope")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Once the user's access changes, the old token can no longer be exchanged.
        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            (await db.Principals.SingleAsync(p => p.Id == userId, Ct)).BumpAccessVersion();
            await db.SaveChangesAsync(Ct);
        });
        (await ExchangeAsync(host, userToken, PlatformServices.PurchaseRequest.Audience)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
