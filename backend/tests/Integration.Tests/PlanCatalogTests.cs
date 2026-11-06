using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Catalog;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Saaskit.Integration.Tests.OperatorApi;

namespace Saaskit.Integration.Tests;

/// <summary>The Subscriptions page's API: plans live in the database, the operator adds and retires them, and module changes reach tenants.</summary>
[Trait("service", "tenancy")]
public sealed class PlanCatalogTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object NewPlan(string key, string name = "Starter") => new { planKey = key, displayName = name, description = "For small teams", monthlyPrice = 49.5m };

    private static async Task<JsonElement> PlanAsync(HttpClient op, int id) =>
        (await Read(await op.GetAsync($"{Api}/plans", Ct))).EnumerateArray().Single(p => p.GetProperty("planId").GetInt32() == id);

    [Fact]
    public async Task Plans_are_seeded_from_platform_yaml_and_a_new_plan_can_onboard_a_tenant_until_it_is_retired()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);

        // Seeded once from platform.yaml: the key, a title-case name, the platform currency and no price.
        var seeded = (await Read(await op.GetAsync($"{Api}/plans", Ct))).EnumerateArray().ToList();
        seeded.Select(p => p.GetProperty("planKey").GetString()).ShouldBe(Plans.All);
        seeded[0].GetProperty("displayName").GetString().ShouldBe(char.ToUpperInvariant(Plans.All[0][0]) + Plans.All[0][1..]);
        seeded[0].GetProperty("currency").GetString().ShouldBe(PlatformInfo.DefaultCurrency);
        seeded[0].GetProperty("monthlyPrice").ValueKind.ShouldBe(JsonValueKind.Null);
        seeded.Select(p => p.GetProperty("sortOrder").GetInt32()).ShouldBe(Enumerable.Range(1, Plans.All.Count));
        seeded[0].GetProperty("modules").EnumerateArray().Select(m => m.GetString()).Order().ShouldBe(Modules.All.Where(m => m.Plans.Contains(Plans.All[0])).Select(m => m.Key).Order());
        (await Read(await op.GetAsync($"{Api}/modules", Ct))).EnumerateArray().Select(m => m.GetProperty("moduleKey").GetString()).Order().ShouldBe(Modules.All.Select(m => m.Key).Order());

        var created = await op.PostAsJsonAsync($"{Api}/plans", NewPlan("starter-test"), Ct);
        var plan = await Read(created, HttpStatusCode.Created);
        var id = plan.GetProperty("planId").GetInt32();
        plan.GetProperty("monthlyPrice").GetDecimal().ShouldBe(49.5m);
        plan.GetProperty("sortOrder").GetInt32().ShouldBe(Plans.All.Count + 1);
        plan.GetProperty("modules").GetArrayLength().ShouldBe(0);
        ETag(created).ShouldNotBeEmpty();

        await Read(await op.PutAsync($"{Api}/plans/{id}/modules/{Modules.AccessManagement}", null, Ct));
        var tenantId = await OnboardAsync(op, "starter-co", "starter-test");
        var tenant = await Read(await op.GetAsync($"{Api}/tenants/{tenantId}", Ct));
        tenant.GetProperty("plan").GetString().ShouldBe("starter-test");
        tenant.GetProperty("planName").GetString().ShouldBe("Starter");
        tenant.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldBe([Modules.AccessManagement]);
        (await PlanAsync(op, id)).GetProperty("tenantCount").GetInt32().ShouldBe(1);

        // Retiring: new tenants and plan changes can't choose it; the tenant already on it stays.
        var retired = await Read(await PutWithETagAsync(op, $"{Api}/plans/{id}/active", new { active = false }, ETag(created)));
        retired.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        var refused = await Read(await op.PostAsJsonAsync($"{Api}/tenants", new { slug = "late-co", displayName = "Late", plan = "starter-test", adminEmail = "a@late.example.com", adminName = "A" }, Ct), HttpStatusCode.UnprocessableEntity);
        refused.GetProperty("code").GetString().ShouldBe("tenant-settings.plan-retired");
        var other = await OnboardAsync(op, "other-co", Fixture.BasePlan);
        (await op.PutAsJsonAsync($"{Api}/tenants/{other}/plan", new { plan = "starter-test" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Read(await op.GetAsync($"{Api}/tenants/{tenantId}", Ct))).GetProperty("plan").GetString().ShouldBe("starter-test");
        // A plan that never existed is a different error from a retired one.
        (await Read(await op.PostAsJsonAsync($"{Api}/tenants", new { slug = "ghost-co", displayName = "Ghost", plan = "no-such-plan", adminEmail = "a@ghost.example.com", adminName = "A" }, Ct), HttpStatusCode.UnprocessableEntity))
            .GetProperty("code").GetString().ShouldBe("tenant-settings.unknown-plan");

        // Restoring makes it choosable again.
        var restored = await Read(await PutWithETagAsync(op, $"{Api}/plans/{id}/active", new { active = true }, retired.GetProperty("eTag").GetString()));
        restored.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        await OnboardAsync(op, "again-co", "starter-test");
    }

    [Fact]
    public async Task Plan_edits_validate_input_use_ETags_and_are_audited()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);

        var created = await op.PostAsJsonAsync($"{Api}/plans", NewPlan("team"), Ct);
        var plan = await Read(created, HttpStatusCode.Created);
        var id = plan.GetProperty("planId").GetInt32();

        (await op.PostAsJsonAsync($"{Api}/plans", NewPlan("team"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var bad = await Read(await op.PostAsJsonAsync($"{Api}/plans", new { planKey = "Not A Key", displayName = "", monthlyPrice = -1, currency = "XX" }, Ct), HttpStatusCode.BadRequest);
        bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ShouldBe(["currency", "displayName", "monthlyPrice", "planKey"]);

        var update = new { displayName = "Team", description = (string?)null, monthlyPrice = 99m, currency = "SGD", sortOrder = 9 };
        (await PutWithETagAsync(op, $"{Api}/plans/{id}", update, null)).StatusCode.ShouldBe((HttpStatusCode)428);
        var changed = await Read(await PutWithETagAsync(op, $"{Api}/plans/{id}", update, ETag(created)));
        changed.GetProperty("displayName").GetString().ShouldBe("Team");
        changed.GetProperty("currency").GetString().ShouldBe("SGD");
        changed.GetProperty("sortOrder").GetInt32().ShouldBe(9);
        changed.GetProperty("description").ValueKind.ShouldBe(JsonValueKind.Null);
        // The old ETag no longer matches.
        (await PutWithETagAsync(op, $"{Api}/plans/{id}", update, ETag(created))).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await PutWithETagAsync(op, $"{Api}/plans/9999", update, "\"x\"")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        var kinds = await db.AuditLog.Select(a => a.Kind).ToListAsync(Ct);
        kinds.ShouldContain("plan-created");
        kinds.ShouldContain("plan-changed");
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var stored = await catalog.Plans.SingleAsync(p => p.PlanId == id, Ct);
        stored.CreatedBy.ShouldNotBe("system");   // the operator, not a background job
        stored.UpdatedOn.ShouldNotBeNull();
    }

    [Fact]
    public async Task Changing_a_plans_modules_changes_every_tenant_on_it_and_tells_the_services()
    {
        var heard = new HashSet<Guid>();
        // The template has only the platform's own modules, which are locked in every plan; a product's modules are not. Billing stands in for one.
        await using var host = await TenancyTestHost.StartAsync(databases, builder =>
        {
            builder.Services.AddSingleton<ITenantChangeListener>(new Recorder(heard));
            builder.Services.AddSingleton(new LockedPlanModules(new HashSet<string>()));
        });
        var op = await SignInAsync(host);
        var plan = await Read(await op.PostAsJsonAsync($"{Api}/plans", NewPlan("team"), Ct), HttpStatusCode.Created);
        var id = plan.GetProperty("planId").GetInt32();
        await Read(await op.PutAsync($"{Api}/plans/{id}/modules/{Modules.AccessManagement}", null, Ct));

        var a = await OnboardAsync(op, "team-a", "team");
        var b = await OnboardAsync(op, "team-b", "team");
        var elsewhere = await OnboardAsync(op, "elsewhere", Fixture.BasePlan);
        // Billing is an add-on on one tenant before the plan has it: adding it to the plan must not duplicate it,
        // and removing it from the plan takes the add-on away too.
        await Read(await op.PutAsync($"{Api}/tenants/{a}/modules/{Modules.Billing}", null, Ct));

        async Task<string[]> ModulesOf(Guid tenant) =>
            [.. (await Read(await op.GetAsync($"{Api}/tenants/{tenant}", Ct))).GetProperty("modules").EnumerateArray().Select(m => m.GetString()!).Order(StringComparer.Ordinal)];
        var elsewhereBefore = await ModulesOf(elsewhere);

        lock (heard)
            heard.Clear();
        var added = await Read(await op.PutAsync($"{Api}/plans/{id}/modules/{Modules.Billing}", null, Ct));
        added.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldContain(Modules.Billing);
        (await ModulesOf(b)).ShouldBe([Modules.AccessManagement, Modules.Billing]);
        (await ModulesOf(a)).ShouldBe([Modules.AccessManagement, Modules.Billing]);
        (await ModulesOf(elsewhere)).ShouldBe(elsewhereBefore);
        // Published on this instance before the call returns; other instances hear it over Redis.
        lock (heard)
        {
            heard.ShouldBe([a, b], ignoreOrder: true);
            heard.Clear();
        }

        // The tenants' own view of their entitlements follows at once, not only the console's.
        var directory = host.Services.GetRequiredService<ITenantDirectory>();
        (await directory.FindByIdAsync(b, Ct))!.EnabledModules.ShouldContain(Modules.Billing);

        // Removing takes it from the plan and from everyone on it, add-ons included. Doing it twice changes nothing more.
        await Read(await op.DeleteAsync($"{Api}/plans/{id}/modules/{Modules.Billing}", Ct));
        lock (heard)
            heard.ShouldBe([a, b], ignoreOrder: true);
        (await ModulesOf(a)).ShouldBe([Modules.AccessManagement]);
        (await ModulesOf(b)).ShouldBe([Modules.AccessManagement]);
        (await directory.FindByIdAsync(a, Ct))!.EnabledModules.ShouldNotContain(Modules.Billing);
        await Read(await op.DeleteAsync($"{Api}/plans/{id}/modules/{Modules.Billing}", Ct));
        (await op.PutAsync($"{Api}/plans/{id}/modules/no-such-module", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Every affected tenant has the change in its own history, and the platform log has one entry for the change.
        var activity = (await Read(await op.GetAsync($"{Api}/tenants/{b}/activity", Ct))).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).ToList();
        activity.ShouldContain("plan-module-added");
        activity.ShouldContain("plan-module-removed");
    }

    [Fact]
    public async Task The_platforms_own_modules_cannot_be_taken_out_of_a_plan_but_can_be_added_to_one()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var seeded = (await Read(await op.GetAsync($"{Api}/plans", Ct))).EnumerateArray().First();
        var id = seeded.GetProperty("planId").GetInt32();

        // Locked: the seeded plan has the module, and the server refuses to take it out.
        seeded.GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldContain(Modules.Billing);
        var refused = await op.DeleteAsync($"{Api}/plans/{id}/modules/{Modules.Billing}", Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync(Ct)).ShouldContain("tenant-settings.module-required-in-plan");
        (await Read(await op.GetAsync($"{Api}/plans", Ct))).EnumerateArray().First().GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldContain(Modules.Billing);

        // A plan an operator creates starts empty, and the platform's modules can still be added to it.
        var created = await Read(await op.PostAsJsonAsync($"{Api}/plans", NewPlan("fresh"), Ct), HttpStatusCode.Created);
        var freshId = created.GetProperty("planId").GetInt32();
        (await Read(await op.PutAsync($"{Api}/plans/{freshId}/modules/{Modules.Billing}", null, Ct))).GetProperty("modules").EnumerateArray().Select(m => m.GetString()).ShouldContain(Modules.Billing);
    }

    private sealed class Recorder(HashSet<Guid> heard) : ITenantChangeListener
    {
        public void OnTenantChanged(Guid tenantId)
        {
            lock (heard)
                heard.Add(tenantId);
        }
    }

    [Fact]
    public async Task Every_plan_route_needs_an_operator_session()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        _ = await SignInAsync(host);
        var anonymous = host.Client();
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "plans"),
            (HttpMethod.Post, "plans"),
            (HttpMethod.Put, "plans/1"),
            (HttpMethod.Put, "plans/1/active"),
            (HttpMethod.Put, $"plans/1/modules/{Modules.Billing}"),
            (HttpMethod.Delete, $"plans/1/modules/{Modules.Billing}"),
            (HttpMethod.Get, "modules"),
            (HttpMethod.Get, "identity-providers"),
            (HttpMethod.Post, "identity-providers"),
            (HttpMethod.Put, "identity-providers/1"),
            (HttpMethod.Put, "identity-providers/1/enabled"),
            (HttpMethod.Delete, "identity-providers/1"),
            (HttpMethod.Delete, $"principals/{Guid.NewGuid()}/providers/1"),
            (HttpMethod.Get, "me"),
            (HttpMethod.Put, "me/preferences"),
        ];
        foreach (var (method, path) in routes)
        {
            using var request = new HttpRequestMessage(method, $"{Api}/{path}");
            if (method == HttpMethod.Post || method == HttpMethod.Put)
                request.Content = JsonContent.Create(new { });
            (await anonymous.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
        }
    }
}
