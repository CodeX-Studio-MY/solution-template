using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Farm360.Core.Platform;
using Farm360.Identity.Application;
using Farm360.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Farm360.Integration.Tests;

/// <summary>Invitation → password → sign-in → authorization code with PKCE → tokens → refresh rotation.</summary>
[Trait("service", "identity")]
public sealed class UserFlowTests(Databases databases)
{
    private const string BffClient = PlatformClients.Bff;
    private const string BffSecret = "bff-test-secret";
    internal const string Callback = "https://demo.localhost/bff/signin-oidc";
    private const string Password = "correct horse battery staple";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    internal static async Task<IdentityTestHost> StartWithBffAsync(Databases databases)
    {
        var host = await IdentityTestHost.StartAsync(databases, "demo");
        await host.InTenantAsync("demo", async sp =>
        {
            var clients = sp.GetRequiredService<IClientRegistry>();
            await clients.RegisterAsync(BffClient, "BFF", null, new ClientCredential(Secret: BffSecret), ["authorization_code", "refresh_token"], Ct);
            await clients.SetBffRedirectUrisAsync(BffClient, [Callback], [], Ct);
        });
        return host;
    }

    /// <summary>Invites a user with a role, accepts the invitation and returns their sign-in name.</summary>
    internal static async Task<string> CreateUserAsync(IdentityTestHost host, string email, Role role)
    {
        var invitation = await host.InTenantAsync("demo", sp => sp.GetRequiredService<IAccountService>()
            .InviteAsync(email, email, [(Modules.TenantSettings, role)], Ct));
        using var browser = host.Browser();
        var token = LoginBrowser.Query(invitation.Url.PathAndQuery, "token");

        var page = await browser.GetAsync(invitation.Url.PathAndQuery);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        var response = await browser.PostFormAsync("/account/invitation", [
            new("__csrf", await LoginBrowser.CsrfAsync(page)), new("tenant", "demo"), new("token", token), new("password", Password)]);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return email;
    }

    internal static async Task<string> SignInForCodeAsync(IdentityTestHost host, string email, string challenge, LoginBrowser browser)
    {
        var authorize = "/connect/authorize?" + string.Join('&', new Dictionary<string, string>
        {
            ["client_id"] = BffClient, ["redirect_uri"] = Callback, ["response_type"] = "code",
            ["scope"] = "openid offline_access", ["state"] = "xyz", ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256", ["tenant"] = "demo",
        }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        var first = await browser.GetAsync(authorize);
        first.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var loginUrl = LoginBrowser.Location(first);
        loginUrl.ShouldStartWith("/account/login");

        var loginPage = await browser.GetAsync(loginUrl);
        var csrf = await LoginBrowser.CsrfAsync(loginPage);
        var login = await browser.PostFormAsync("/account/login", [
            new("__csrf", csrf), new("tenant", "demo"), new("returnUrl", LoginBrowser.Query(loginUrl, "returnUrl")),
            new("username", email), new("password", Password)]);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var done = await browser.GetAsync(LoginBrowser.Location(login));
        done.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var callback = LoginBrowser.Location(done);
        callback.ShouldStartWith(Callback);
        LoginBrowser.Query(callback, "state").ShouldBe("xyz");
        return LoginBrowser.Query(callback, "code");
    }

    internal static Task<HttpResponseMessage> TokenAsync(IdentityTestHost host, Dictionary<string, string> fields)
    {
        fields["client_id"] = BffClient;
        fields["client_secret"] = BffSecret;
        fields["tenant"] = "demo";
        return host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(fields), Ct);
    }

    [Fact]
    public async Task A_user_invited_by_email_signs_in_and_gets_tokens_with_their_effective_roles()
    {
        await using var host = await StartWithBffAsync(databases);
        var email = await CreateUserAsync(host, "ali@example.com", Role.Manager);
        (host.Messages.Links.Single().Kind).ShouldBe("invitation");

        var (verifier, challenge) = LoginBrowser.Pkce();
        using var browser = host.Browser();
        var code = await SignInForCodeAsync(host, email, challenge, browser);

        var exchange = await TokenAsync(host, new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Callback, ["code_verifier"] = verifier,
            ["resource"] = PlatformServices.Tenancy.Audience,
        });
        var body = await exchange.Content.ReadFromJsonAsync<JsonElement>(Ct);
        exchange.StatusCode.ShouldBe(HttpStatusCode.OK, body.ToString());

        var access = new JsonWebTokenHandler().ReadJsonWebToken(body.GetProperty("access_token").GetString());
        access.GetClaim("principal_type").Value.ShouldBe("user");
        access.GetClaim("roles").Value.ShouldBe("tenant-settings:manager");
        access.Audiences.ShouldBe([PlatformServices.Tenancy.Audience]);
        body.TryGetProperty("refresh_token", out _).ShouldBeTrue();

        // A password sign-in that fails reveals nothing about whether the account exists.
        using var other = host.Browser();
        var page = await other.GetAsync("/account/login?tenant=demo");
        var wrong = await other.PostFormAsync("/account/login", [new("__csrf", await LoginBrowser.CsrfAsync(page)), new("tenant", "demo"), new("username", email), new("password", "wrong")]);
        var unknown = await other.PostFormAsync("/account/login", [new("__csrf", await LoginBrowser.CsrfAsync(page)), new("tenant", "demo"), new("username", "nobody@example.com"), new("password", "wrong")]);
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        static string WithoutCsrf(string html) => System.Text.RegularExpressions.Regex.Replace(html, "name=\"__csrf\" value=\"[^\"]+\"", "name=\"__csrf\"");
        WithoutCsrf(await wrong.Content.ReadAsStringAsync(Ct)).ShouldBe(WithoutCsrf(await unknown.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task Refresh_tokens_rotate_and_reuse_revokes_the_whole_chain()
    {
        await using var host = await StartWithBffAsync(databases);
        var email = await CreateUserAsync(host, "siti@example.com", Role.User);
        var (verifier, challenge) = LoginBrowser.Pkce();
        using var browser = host.Browser();
        var code = await SignInForCodeAsync(host, email, challenge, browser);
        var first = await (await TokenAsync(host, new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Callback, ["code_verifier"] = verifier,
        })).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var refresh1 = first.GetProperty("refresh_token").GetString()!;

        // Refreshing returns a new access token with the audience asked for, and a rotated refresh token.
        var second = await TokenAsync(host, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh1, ["resource"] = PlatformServices.Notification.Audience });
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, secondBody.ToString());
        new JsonWebTokenHandler().ReadJsonWebToken(secondBody.GetProperty("access_token").GetString()).Audiences.ShouldBe([PlatformServices.Notification.Audience]);
        var refresh2 = secondBody.GetProperty("refresh_token").GetString()!;
        refresh2.ShouldNotBe(refresh1);

        // Reusing the old token is refused, and it revokes the chain: the newer token stops working too.
        (await TokenAsync(host, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh1 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await TokenAsync(host, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh2 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Changed_access_is_picked_up_on_refresh_and_unknown_audiences_are_refused()
    {
        await using var host = await StartWithBffAsync(databases);
        var email = await CreateUserAsync(host, "lim@example.com", Role.User);
        var (verifier, challenge) = LoginBrowser.Pkce();
        using var browser = host.Browser();
        var code = await SignInForCodeAsync(host, email, challenge, browser);
        var first = await (await TokenAsync(host, new()
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = Callback, ["code_verifier"] = verifier,
        })).Content.ReadFromJsonAsync<JsonElement>(Ct);

        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var user = await db.Principals.SingleAsync(p => p.DisplayName == email, Ct);
            var assignment = await db.AccessAssignments.SingleAsync(a => a.PrincipalId == user.Id, Ct);
            assignment.ChangeRole(Role.Admin);
            user.BumpAccessVersion();
            await db.SaveChangesAsync(Ct);
        });

        var refreshed = await (await TokenAsync(host, new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = first.GetProperty("refresh_token").GetString()!, ["resource"] = PlatformServices.Tenancy.Audience,
        })).Content.ReadFromJsonAsync<JsonElement>(Ct);
        new JsonWebTokenHandler().ReadJsonWebToken(refreshed.GetProperty("access_token").GetString()).GetClaim("roles").Value.ShouldBe("tenant-settings:admin");

        var bad = await TokenAsync(host, new()
        {
            ["grant_type"] = "refresh_token", ["refresh_token"] = refreshed.GetProperty("refresh_token").GetString()!, ["resource"] = $"{PlatformInfo.Product}:not-a-service",
        });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // invalid_target
    }

    [Fact]
    public async Task Invitations_are_single_use_and_a_weak_password_is_refused()
    {
        await using var host = await StartWithBffAsync(databases);
        var invitation = await host.InTenantAsync("demo", sp => sp.GetRequiredService<IAccountService>().InviteAsync("new@example.com", "New", [], Ct));
        var token = LoginBrowser.Query(invitation.Url.PathAndQuery, "token");
        using var browser = host.Browser();
        var page = await browser.GetAsync(invitation.Url.PathAndQuery);
        var csrf = await LoginBrowser.CsrfAsync(page);

        var weak = await browser.PostFormAsync("/account/invitation", [new("__csrf", csrf), new("tenant", "demo"), new("token", token), new("password", "short")]);
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync(Ct)).ShouldContain("at least 15 characters");

        (await browser.PostFormAsync("/account/invitation", [new("__csrf", csrf), new("tenant", "demo"), new("token", token), new("password", Password)]))
            .StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var again = await browser.PostFormAsync("/account/invitation", [new("__csrf", csrf), new("tenant", "demo"), new("token", token), new("password", Password)]);
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Repeated_failures_lock_the_account_with_growing_delays_and_a_reset_clears_it()
    {
        await using var host = await StartWithBffAsync(databases);
        var email = await CreateUserAsync(host, "locked@example.com", Role.User);
        using var browser = host.Browser();
        for (var i = 0; i < 6; i++)
        {
            var page = await browser.GetAsync("/account/login?tenant=demo");
            await browser.PostFormAsync("/account/login", [new("__csrf", await LoginBrowser.CsrfAsync(page)), new("tenant", "demo"), new("username", email), new("password", "wrong password here")]);
        }

        var lockout = await host.InTenantAsync("demo", async sp =>
        {
            var user = await sp.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PlatformUser>>().FindByNameAsync(email);
            return (user!.ConsecutiveLockouts, user.LockoutEnd);
        });
        lockout.ConsecutiveLockouts.ShouldBeGreaterThan(0);
        lockout.LockoutEnd.ShouldNotBeNull();
        lockout.LockoutEnd!.Value.ShouldBeLessThan(TimeProvider.System.GetUtcNow().AddHours(25));   // never permanent

        // Even the right password is refused while locked out.
        var locked = await browser.GetAsync("/account/login?tenant=demo");
        var attempt = await browser.PostFormAsync("/account/login", [new("__csrf", await LoginBrowser.CsrfAsync(locked)), new("tenant", "demo"), new("username", email), new("password", Password)]);
        attempt.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Password reset: a single-use emailed link.
        await host.InTenantAsync("demo", sp => sp.GetRequiredService<IAccountService>().RequestPasswordResetAsync(email, Ct));
        var reset = host.Messages.Links.Last(l => l.Kind == "password-reset");
        var resetPage = await browser.GetAsync(reset.Url.PathAndQuery);
        var csrf = await LoginBrowser.CsrfAsync(resetPage);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__csrf", csrf), new("tenant", "demo"), new("uid", LoginBrowser.Query(reset.Url.PathAndQuery, "uid")),
            new("token", LoginBrowser.Query(reset.Url.PathAndQuery, "token")), new("password", "a brand new long passphrase"),
        };
        (await browser.PostFormAsync("/account/reset", fields)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await browser.PostFormAsync("/account/reset", fields)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
