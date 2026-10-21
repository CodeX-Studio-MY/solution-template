using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Saaskit.Integration.Tests;

/// <summary>The operator console's BFF: the session token stays in an HttpOnly cookie on the operator host and is added to proxied calls.</summary>
[Trait("service", "gateway")]
public sealed class OperatorBffTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string OperatorHost = "operator.localhost";

    private static Task<GatewayTestHost> StartAsync(Databases databases) => GatewayTestHost.StartAsync(databases, services =>
    {
        // Tenancy's sign-in: one valid operator. The proxied API echoes what reached it.
        services.AddHttpClient("operator").ConfigurePrimaryHttpMessageHandler(() => new Stub(async request =>
        {
            var body = await request.Content!.ReadFromJsonAsync<JsonElement>(Ct);
            return body.GetProperty("password").GetString() == "right"
                ? Json(HttpStatusCode.OK, new { token = "tok-1", expiresOn = TimeProvider.System.GetUtcNow().UtcDateTime.AddHours(8) })
                : Json(HttpStatusCode.Unauthorized, new { title = "tenant-settings.operator-login-failed", status = 401 });
        }));
        services.AddSingleton<IForwarderHttpClientFactory>(new EchoForwarder());
    }, "demo");

    private static HttpRequestMessage Post(string url, object? body = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (csrf)
            request.Headers.Add("X-CSRF", "1");
        return request;
    }

    [Fact]
    public async Task Signing_in_sets_an_HttpOnly_cookie_and_proxied_calls_carry_the_operator_token()
    {
        await using var host = await StartAsync(databases);
        using var browser = host.Browser(OperatorHost);

        (await browser.GetAsync("/api/tenancy/v1/operator/tenants")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await browser.SendAsync(Post("/bff/operator/login", new { email = "o@example.com", password = "right", totpCode = "000000" }, csrf: false))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await browser.SendAsync(Post("/bff/operator/login", new { email = "o@example.com", password = "wrong", totpCode = "000000" }))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var login = await browser.SendAsync(Post("/bff/operator/login", new { email = "o@example.com", password = "right", totpCode = "000000" }));
        login.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-operator=", StringComparison.Ordinal));
        setCookie.ShouldContain("httponly");
        setCookie.ShouldContain("samesite=strict");
        setCookie.ShouldNotContain("tok-1");   // encrypted, never the raw token

        var echoed = await (await browser.GetAsync("/api/tenancy/v1/operator/tenants")).Content.ReadFromJsonAsync<JsonElement>(Ct);
        echoed.GetProperty("authorization").GetString().ShouldBe("Operator tok-1");
        echoed.GetProperty("cookie").GetString().ShouldBeEmpty();

        (await browser.SendAsync(Post("/bff/operator/logout"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_operator_endpoints_and_cookie_mean_nothing_on_a_tenant_host()
    {
        await using var host = await StartAsync(databases);
        using var operatorBrowser = host.Browser(OperatorHost);
        await operatorBrowser.SendAsync(Post("/bff/operator/login", new { email = "o@example.com", password = "right", totpCode = "000000" }));

        using var tenant = host.Browser("demo.localhost");
        (await tenant.SendAsync(Post("/bff/operator/login", new { email = "o@example.com", password = "right", totpCode = "000000" }))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        tenant.SetCookie("__Host-operator", operatorBrowser.CookieNamed("__Host-operator"));
        // On a tenant host the operator path is just the tenancy service's route, which needs a tenant session.
        (await tenant.GetAsync("/api/tenancy/v1/operator/tenants")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status) { Content = JsonContent.Create(body) };

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }

    private sealed class EchoForwarder : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new Stub(request => Task.FromResult(Json(HttpStatusCode.OK, new
        {
            authorization = request.Headers.Authorization?.ToString() ?? "",
            cookie = request.Headers.TryGetValues("Cookie", out var c) ? string.Join(";", c) : "",
        }))));
    }
}
