using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Integration.Tests;

/// <summary>A tenant's name, time zone, currency and locale: changed by its own admin or by an operator, validated.</summary>
[Trait("service", "tenancy")]
public sealed class TenantProfileTests(Databases databases)
{
    private const string Api = "/api/tenancy/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Admins_and_operators_change_the_profile_and_bad_values_are_refused()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        await using var scope = host.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync("ops@example.com", "Ops", Ct);
        var op = host.Client();
        var code = Totp.Compute(created.TotpSecret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30);
        var session = await Read(await op.PostAsJsonAsync($"{Api}/operator/sessions", new { email = "ops@example.com", password = created.Password, totpCode = code }, Ct));
        op.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Operator {session.GetProperty("token").GetString()}");
        var tenantId = (await Read(await op.PostAsJsonAsync($"{Api}/operator/tenants", new { slug = "prof", displayName = "Prof", plan = Fixture.BasePlan, adminEmail = "a@prof.example.com", adminName = "A" }, Ct), HttpStatusCode.Created))
            .GetProperty("tenantId").GetGuid();

        HttpClient Caller(string role)
        {
            var client = host.Client("prof.localhost");
            client.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenantId, roles: [$"{Modules.TenantSettings}:{role}"], audience: PlatformServices.Tenancy.Audience));
            return client;
        }

        var admin = Caller("admin");
        var changed = await Read(await admin.PutAsJsonAsync($"{Api}/me/tenant/profile", new { displayName = "Prof Sdn Bhd", timeZone = "Asia/Singapore", currency = "sgd", locale = "zh" }, Ct));
        changed.GetProperty("displayName").GetString().ShouldBe("Prof Sdn Bhd");
        changed.GetProperty("currency").GetString().ShouldBe("SGD");
        (await Read(await Caller("user").GetAsync($"{Api}/me/tenant", Ct))).GetProperty("timeZone").GetString().ShouldBe("Asia/Singapore");

        var bad = await Read(await admin.PutAsJsonAsync($"{Api}/me/tenant/profile", new { displayName = "", timeZone = "Mars/Base", currency = "XYZ1", locale = "fr" }, Ct), HttpStatusCode.BadRequest);
        bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ShouldBe(["currency", "displayName", "locale", "timeZone"]);

        (await Caller("user").PutAsJsonAsync($"{Api}/me/tenant/profile", new { displayName = "X", timeZone = "UTC", currency = "MYR", locale = "en" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Read(await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/profile", new { displayName = "Prof", timeZone = "UTC", currency = "MYR", locale = "en" }, Ct)))
            .GetProperty("locale").GetString().ShouldBe("en");
    }
}
