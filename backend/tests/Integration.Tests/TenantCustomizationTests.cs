using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Farm360.Core.Catalog;
using Farm360.Core.Platform;
using Farm360.Tenancy.Application;
using Farm360.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Integration.Tests;

/// <summary>Rungs 1 and 2: a tenant's settings and feature flags are data, edited by operators and (the allowed ones) by the tenant's own admin, and audited.</summary>
[Trait("service", "tenancy")]
public sealed class TenantCustomizationTests(Databases databases)
{
    private const string Api = "/api/tenancy/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    private static async Task<HttpClient> OperatorAsync(TenancyTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync("ops@example.com", "Ops One", Ct);
        var client = host.Client();
        var code = Totp.Compute(created.TotpSecret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30);
        var session = await Read(await client.PostAsJsonAsync($"{Api}/operator/sessions", new { email = "ops@example.com", password = created.Password, totpCode = code }, Ct));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Operator {session.GetProperty("token").GetString()}");
        return client;
    }

    private static HttpClient Caller(TenancyTestHost host, Guid tenantId, string slug, params string[] roles)
    {
        var client = host.Client($"{slug}.localhost");
        client.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenantId, roles: roles, audience: PlatformServices.Tenancy.Audience));
        return client;
    }

    private static async Task<Guid> OnboardAsync(HttpClient op, string slug)
    {
        var result = await Read(await op.PostAsJsonAsync($"{Api}/operator/tenants", new { slug, displayName = slug, plan = Fixture.BasePlan, adminEmail = $"admin@{slug}.example.com", adminName = "Admin" }, Ct), HttpStatusCode.Created);
        return result.GetProperty("tenantId").GetGuid();
    }

    [Fact]
    public async Task A_new_tenant_works_with_no_configuration_and_everything_is_default()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = await OnboardAsync(op, "fresh");

        var ui = await Read(await Caller(host, tenantId, "fresh", $"{Modules.TenantSettings}:user").GetAsync($"{Api}/me/settings", Ct));
        ui.GetProperty("branding").GetProperty("primaryColor").GetString().ShouldBe("");
        ui.GetProperty("features").GetProperty(FixtureFeatures.DraftSharing).GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Operators_change_any_setting_and_it_reaches_the_tenant_and_the_audit_log()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = await OnboardAsync(op, "plus");

        var changed = await Read(await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/settings/{Fixture.Entitled}",
            new { autoApproveBelow = 200, approvalPolicy = "two-level" }, Ct));
        changed.GetProperty("values").GetProperty("autoApproveBelow").GetDecimal().ShouldBe(200m);
        changed.GetProperty("values").GetProperty("approvalPolicy").GetString().ShouldBe("two-level");
        changed.GetProperty("values").GetProperty("secondApprovalAbove").GetDecimal().ShouldBe(5000m, "what the tenant didn't set stays at the default");

        // The stored override has only what differs; a null puts a setting back to its default.
        var reset = await Read(await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/settings/{Fixture.Entitled}", new { approvalPolicy = (string?)null }, Ct));
        reset.GetProperty("values").GetProperty("approvalPolicy").GetString().ShouldBe("single");
        reset.GetProperty("overrides").EnumerateObject().Select(p => p.Name).ShouldBe(["autoApproveBelow"]);

        await using var scope = host.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>().AuditLog.AsNoTracking().Where(a => a.TenantId == tenantId && a.Kind == "settings-changed").ToListAsync(Ct);
        audit.Count.ShouldBe(2);
        audit.ShouldAllBe(a => a.Actor != null);
    }

    [Fact]
    public async Task Bad_values_are_refused_with_the_reason()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = await OnboardAsync(op, "bad");
        var url = $"{Api}/operator/tenants/{tenantId}/settings/{Fixture.Entitled}";

        (await op.PutAsJsonAsync(url, new { autoApproveBelow = -5 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await op.PutAsJsonAsync(url, new { approvalPolicy = "does-not-exist" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await op.PutAsJsonAsync(url, new { notASetting = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/settings/nope", new { a = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/settings/{Modules.TenantSettings}", new { primaryColor = "red" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/settings/{Modules.TenantSettings}", new { logoUrl = "https://evil.example/x.png" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_tenant_admin_changes_only_the_settings_marked_editable()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = await OnboardAsync(op, "self");
        var admin = Caller(host, tenantId, "self", $"{Modules.TenantSettings}:admin");

        var branding = await Read(await admin.PutAsJsonAsync($"{Api}/settings/{Modules.TenantSettings}", new { primaryColor = "#aa3300" }, Ct));
        branding.GetProperty("values").GetProperty("primaryColor").GetString().ShouldBe("#aa3300");

        // The strategy is the operator's call, not the tenant's.
        var strategy = await admin.PutAsJsonAsync($"{Api}/settings/{Fixture.Entitled}", new { approvalPolicy = "two-level" }, Ct);
        strategy.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        // The threshold is one the tenant may set itself.
        (await admin.PutAsJsonAsync($"{Api}/settings/{Fixture.Entitled}", new { autoApproveBelow = 250 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The UI sees branding straight away; settings not exposed to the UI stay out of it.
        var ui = await Read(await Caller(host, tenantId, "self", $"{Modules.TenantSettings}:user").GetAsync($"{Api}/me/settings", Ct));
        ui.GetProperty("branding").GetProperty("primaryColor").GetString().ShouldBe("#aa3300");
        ui.GetProperty("values").TryGetProperty(Fixture.Entitled, out _).ShouldBeFalse();

        // Ordinary users can't read or change settings.
        var user = Caller(host, tenantId, "self", $"{Modules.TenantSettings}:user");
        (await user.GetAsync($"{Api}/settings", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await user.PutAsJsonAsync($"{Api}/settings/{Modules.TenantSettings}", new { primaryColor = "#000000" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Feature_flags_are_per_tenant_and_only_operators_flip_them()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var one = await OnboardAsync(op, "flag-one");
        var two = await OnboardAsync(op, "flag-two");

        var on = await Read(await op.PutAsJsonAsync($"{Api}/operator/tenants/{one}/features/{FixtureFeatures.DraftSharing}", new { enabled = true }, Ct));
        on.GetProperty("effective").GetBoolean().ShouldBeTrue();

        (await Read(await Caller(host, one, "flag-one", $"{Modules.TenantSettings}:user").GetAsync($"{Api}/me/settings", Ct))).GetProperty("features").GetProperty(FixtureFeatures.DraftSharing).GetBoolean().ShouldBeTrue();
        (await Read(await Caller(host, two, "flag-two", $"{Modules.TenantSettings}:user").GetAsync($"{Api}/me/settings", Ct))).GetProperty("features").GetProperty(FixtureFeatures.DraftSharing).GetBoolean().ShouldBeFalse();

        // Back to the default with a null.
        var back = await Read(await op.PutAsJsonAsync($"{Api}/operator/tenants/{one}/features/{FixtureFeatures.DraftSharing}", new { enabled = (bool?)null }, Ct));
        back.GetProperty("override").ValueKind.ShouldBe(JsonValueKind.Null);
        back.GetProperty("effective").GetBoolean().ShouldBeFalse();

        (await op.PutAsJsonAsync($"{Api}/operator/tenants/{one}/features/not-a-flag", new { enabled = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var admin = Caller(host, one, "flag-one", $"{Modules.TenantSettings}:admin");
        (await Read(await admin.GetAsync($"{Api}/features", Ct))).EnumerateArray().Select(f => f.GetProperty("key").GetString()).ShouldContain(FixtureFeatures.DraftSharing);
        (await admin.PutAsJsonAsync($"{Api}/features/{FixtureFeatures.DraftSharing}", new { enabled = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customization_is_stored_per_tenant_in_the_catalog()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var a = await OnboardAsync(op, "iso-a");
        var b = await OnboardAsync(op, "iso-b");
        await op.PutAsJsonAsync($"{Api}/operator/tenants/{a}/settings/{Fixture.Entitled}", new { autoApproveBelow = 999 }, Ct);

        await using var scope = host.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        (await catalog.TenantSettings.CountAsync(s => s.TenantId == a, Ct)).ShouldBe(1);
        (await catalog.TenantSettings.CountAsync(s => s.TenantId == b, Ct)).ShouldBe(0);
    }
}
