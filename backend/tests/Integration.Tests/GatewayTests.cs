using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Farm360.Core.Platform;
using Farm360.Identity.Application;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Role = Farm360.Core.Platform.Role;

namespace Farm360.Integration.Tests;

/// <summary>The Gateway as the BFF: sessions in Redis, no tokens in the browser, CSRF, security headers, per-service tokens.</summary>
[Trait("service", "gateway")]
public sealed class GatewayTests(Databases databases)
{
    private const string Password = "correct horse battery staple";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task CreateUserAsync(GatewayTestHost host, string email)
    {
        var invitation = await host.Identity.InTenantAsync("demo", sp => sp.GetRequiredService<IAccountService>()
            .InviteAsync(email, email, [(Modules.AccessManagement, Role.Manager)], Ct));
        using var login = host.Identity.Browser();
        var page = await login.GetAsync(invitation.Url.PathAndQuery);
        (await login.PostFormAsync("/account/invitation", [
            new("__csrf", await LoginBrowser.CsrfAsync(page)), new("tenant", "demo"),
            new("token", LoginBrowser.Query(invitation.Url.PathAndQuery, "token")), new("password", Password)])).StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    /// <summary>Walks the whole browser flow: Gateway → central sign-in host → back to the Gateway with a code.</summary>
    private static async Task<LoginBrowser> SignInAsync(GatewayTestHost host, string email, string tenantHost = "demo.localhost")
    {
        var gateway = host.Browser(tenantHost);
        using var signIn = host.Identity.Browser();

        var start = await gateway.GetAsync("/bff/login?returnUrl=/dashboard");
        start.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var authorize = new Uri(LoginBrowser.Location(start));
        authorize.Host.ShouldBe("login.localhost");
        LoginBrowser.Query(authorize.PathAndQuery, "tenant").ShouldBe(tenantHost.Split('.')[0]);

        var toLogin = await signIn.GetAsync(authorize.PathAndQuery);
        var loginUrl = LoginBrowser.Location(toLogin);
        var loginPage = await signIn.GetAsync(loginUrl);
        var posted = await signIn.PostFormAsync("/account/login", [
            new("__csrf", await LoginBrowser.CsrfAsync(loginPage)), new("tenant", tenantHost.Split('.')[0]),
            new("returnUrl", LoginBrowser.Query(loginUrl, "returnUrl")), new("username", email), new("password", Password)]);
        posted.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var back = await signIn.GetAsync(LoginBrowser.Location(posted));
        var callback = new Uri(LoginBrowser.Location(back));
        callback.Host.ShouldBe(tenantHost);

        var done = await gateway.GetAsync(callback.PathAndQuery);
        done.StatusCode.ShouldBe(HttpStatusCode.Redirect, await done.Content.ReadAsStringAsync(Ct));
        LoginBrowser.Location(done).ShouldBe("/dashboard");
        return gateway;
    }

    private static HttpRequestMessage Api(HttpMethod method, string path, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, path);
        if (csrf)
            request.Headers.Add("X-CSRF", "1");
        return request;
    }

    [Fact]
    public async Task Sign_in_gives_an_opaque_cookie_and_the_browser_never_sees_a_token()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        await CreateUserAsync(host, "ali@example.com");
        using var browser = host.Browser("demo.localhost");

        (await browser.GetAsync("/bff/user")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var signedIn = await SignInAsync(host, "ali@example.com");
        var user = await signedIn.GetAsync("/bff/user");
        user.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await user.Content.ReadAsStringAsync(Ct);
        var json = JsonDocument.Parse(body).RootElement;
        json.GetProperty("tenant").GetString().ShouldBe("demo");
        json.GetProperty("principalType").GetString().ShouldBe("user");
        json.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldBe(["access-management:manager"]);

        // The cookie is an opaque session ID (not a JWT), and nothing the browser receives carries a token.
        var cookie = signedIn.CookieNamed("__Host-bff");
        cookie.ShouldNotBeEmpty();
        cookie.ShouldNotContain("eyJ");
        body.ShouldNotContain("access_token");
        body.ShouldNotContain("refresh_token");
        body.ShouldNotContain("eyJ");

        // The session lives in Redis, encrypted, not in the cookie.
        var redis = host.Services.GetRequiredService<IConnectionMultiplexer>();
        var keys = redis.GetServer(redis.GetEndPoints()[0]).Keys(pattern: "farm360:bff:session:*").ToList();
        keys.ShouldNotBeEmpty();
        var stored = (byte[])(await redis.GetDatabase().StringGetAsync(keys[0]))!;
        System.Text.Encoding.UTF8.GetString(stored).ShouldNotContain("eyJ");
    }

    [Fact]
    public async Task Calls_to_a_service_carry_a_token_for_that_services_audience_and_the_browsers_headers_are_dropped()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        await CreateUserAsync(host, "siti@example.com");
        using var browser = await SignInAsync(host, "siti@example.com");

        // The user is a manager in access-management, so the Identity API answers.
        var groups = await browser.SendAsync(Api(HttpMethod.Get, "/api/identity/v1/groups"));
        groups.StatusCode.ShouldBe(HttpStatusCode.OK, await groups.Content.ReadAsStringAsync(Ct));

        // Headers a browser tries to set for the platform never reach the service: its own token is ignored and replaced.
        var spoof = Api(HttpMethod.Get, "/api/identity/v1/groups");
        spoof.Headers.TryAddWithoutValidation("Authorization", "Bearer forged.token.value");
        spoof.Headers.TryAddWithoutValidation("X-Tenant", "demo-plus");
        (await browser.SendAsync(spoof)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // A manager can't write; the service says so (403), proving the user's real roles travelled.
        var write = Api(HttpMethod.Post, "/api/identity/v1/groups");
        write.Content = JsonContent.Create(new { displayName = "x" });
        (await browser.SendAsync(write)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task State_changing_requests_need_the_CSRF_header_and_preflights_are_refused()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        await CreateUserAsync(host, "lim@example.com");
        using var browser = await SignInAsync(host, "lim@example.com");

        var forged = Api(HttpMethod.Post, "/api/identity/v1/groups", csrf: false);
        forged.Content = JsonContent.Create(new { displayName = "x" });
        (await browser.SendAsync(forged)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await browser.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/bff/logout"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/identity/v1/groups");
        preflight.Headers.Add("Origin", "https://evil.example");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "x-csrf");
        var answer = await browser.SendAsync(preflight);
        answer.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        answer.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_renewals_share_one_refresh_and_never_trip_reuse_detection()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        await CreateUserAsync(host, "tan@example.com");
        using var browser = await SignInAsync(host, "tan@example.com");

        // Drop the cached access tokens so every request below needs a renewal at the same moment.
        var redis = host.Services.GetRequiredService<IConnectionMultiplexer>();
        foreach (var key in redis.GetServer(redis.GetEndPoints()[0]).Keys(pattern: "farm360:bff:tokens:*").ToList())
            await redis.GetDatabase().KeyDeleteAsync(key);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => browser.SendAsync(Api(HttpMethod.Get, "/api/identity/v1/groups"))));
        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);

        // And the session still works afterwards (the rotated refresh token was kept).
        foreach (var key in redis.GetServer(redis.GetEndPoints()[0]).Keys(pattern: "farm360:bff:tokens:*").ToList())
            await redis.GetDatabase().KeyDeleteAsync(key);
        (await browser.SendAsync(Api(HttpMethod.Get, "/api/identity/v1/groups"))).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_ends_the_session_everywhere()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        await CreateUserAsync(host, "yap@example.com");
        using var browser = await SignInAsync(host, "yap@example.com");
        var cookie = browser.CookieNamed("__Host-bff");

        var logout = await browser.SendAsync(Api(HttpMethod.Post, "/bff/logout"));
        ((int)logout.StatusCode).ShouldBeInRange(200, 399);
        (await browser.SendAsync(Api(HttpMethod.Get, "/api/identity/v1/groups"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Replaying the old cookie doesn't work: the server-side session is gone.
        using var replay = host.Browser("demo.localhost");
        replay.SetCookie("__Host-bff", cookie);
        (await replay.SendAsync(Api(HttpMethod.Get, "/api/identity/v1/groups"))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_session_belongs_to_one_tenant_and_unknown_hosts_are_refused()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo", "demo-plus");
        await CreateUserAsync(host, "ong@example.com");
        using var browser = await SignInAsync(host, "ong@example.com");
        var cookie = browser.CookieNamed("__Host-bff");

        // The same cookie value presented on another tenant's host is not a session there.
        using var other = host.Browser("demo-plus.localhost");
        other.SetCookie("__Host-bff", cookie);
        (await other.GetAsync("/bff/user")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var nobody = host.Browser("nobody.localhost");
        (await nobody.GetAsync("/bff/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers_and_config_is_served_from_the_backend_schema()
    {
        await using var host = await GatewayTestHost.StartAsync(databases, "demo");
        using var browser = host.Browser("demo.localhost");

        var response = await browser.GetAsync("/bff/config");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldContain("max-age=");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").ShouldNotBeEmpty();
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldContain("script-src 'self'");
        csp.ShouldContain("style-src 'self' 'nonce-");
        csp.ShouldContain("connect-src 'self' wss://demo.localhost");

        var config = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;
        config.GetProperty("tenant").GetProperty("slug").GetString().ShouldBe("demo");
        config.GetProperty("defaults").GetProperty("currency").GetString().ShouldBe(PlatformInfo.DefaultCurrency);
        config.GetProperty("modules").GetProperty("baseUrl").GetString().ShouldBe("/mfe");

        // The central sign-in host is reached through the Gateway too, with the same headers.
        using var login = host.Browser("login.localhost");
        var discovery = await login.GetAsync("/.well-known/openid-configuration");
        discovery.StatusCode.ShouldBe(HttpStatusCode.OK);
        discovery.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
}
