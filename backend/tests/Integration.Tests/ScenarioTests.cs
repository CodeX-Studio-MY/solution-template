using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Acme.Contracts;
using Acme.Core.Catalog;
using Acme.Core.Catalog.Entities;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.CostCenterBudget;
using Acme.PurchaseRequest;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Role = Acme.Core.Platform.Role;

namespace Acme.Integration.Tests;

/// <summary>
/// The reference scenario (SAAS_TEMPLATE_PROMPT 7.1 and 7.5): the same code behaves differently for two tenants, and only because of
/// their data. <c>demo</c> (Standard plan) has the defaults; <c>demo-plus</c> (Plus plan) has its own settings, strategy, flag, custom field and the
/// budget module. Both purchase-request and cost-center-budget run in one host, as in AllInOne.Host, over the real broker.
/// </summary>
[Collection(BrokerCollection.Name)]
public sealed class ScenarioTests(Databases databases)
{
    private const string Requests = "/api/purchase-request/v1";
    private const string Budgets = "/api/cost-center-budget/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] CostCenters = ["CC-100", "CC-200"];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly (string, Role)[] AsUser = [(Modules.PurchaseRequest, Role.User)];
    private static readonly (string, Role)[] AsManager = [(Modules.PurchaseRequest, Role.Manager)];
    private static readonly (string, Role)[] AsAdmin = [(Modules.PurchaseRequest, Role.Admin)];
    private static readonly (string, Role)[] AsFinance = [(Modules.CostCenterBudget, Role.Manager), (Modules.PurchaseRequest, Role.User)];

    private async Task<ModuleTestHost> StartAsync(string provider)
    {
        var host = await ModuleTestHost.StartAsync<PurchaseRequestModule, CostCenterBudgetModule>(databases, provider, null, "demo", "demo-plus");

        // The data that makes demo-plus different (what scripts/dev-up seeds). Nothing about it is in code.
        await host.InTenantAsync("demo-plus", async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogDbContext>();
            var tenantId = host.Tenants["demo-plus"].Id;
            var now = TimeProvider.System.GetUtcNow().UtcDateTime;
            catalog.TenantSettings.Add(new TenantSetting
            {
                TenantId = tenantId,
                ModuleKey = Modules.PurchaseRequest,
                Json = """{"autoApproveBelow":200,"approvalPolicy":"two-level"}""",
                UpdatedOn = now,
                UpdatedBy = "test",
            });
            catalog.TenantFeatures.Add(new TenantFeature { TenantId = tenantId, FeatureKey = PurchaseRequestFeatures.DraftSharing, Enabled = true, UpdatedOn = now, UpdatedBy = "test" });
            await catalog.SaveChangesAsync(Ct);
            sp.GetRequiredService<ITenantCustomizationCache>().Invalidate(tenantId);
        });

        // Cost center: a required list field, defined through the admin API like a tenant admin would.
        var admin = host.Client("demo-plus", host.Token("demo-plus", roles: AsAdmin));
        var field = await admin.PutAsJsonAsync($"{Requests}/custom-fields/{Modules.PurchaseRequest}/{WellKnownCustomFields.CostCenter}",
            new { label = "Cost center", type = "list", required = true, options = CostCenters, displayOrder = 1 }, Ct);
        field.StatusCode.ShouldBe(HttpStatusCode.OK, await field.Content.ReadAsStringAsync(Ct));
        return host;
    }

    private static HttpClient Client(ModuleTestHost host, string slug, Guid? user = null, params (string, Role)[] roles) =>
        host.Client(slug, host.Token(slug, user, PrincipalType.User, roles));

    private static object NewRequest(decimal total, string? costCenter = null) => costCenter is null
        ? new { title = "Standing desks", lines = new[] { new { item = "Desk", quantity = 1m, unitPrice = total } } }
        : new { title = "Standing desks", lines = new[] { new { item = "Desk", quantity = 1m, unitPrice = total } }, customFields = new Dictionary<string, string> { [WellKnownCustomFields.CostCenter] = costCenter } };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, object body) =>
        await ReadAsync(await client.PostAsJsonAsync($"{Requests}/requests", body, Ct), HttpStatusCode.Created);

    private static async Task<JsonElement> ActAsync(HttpClient client, JsonElement request, string action)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{Requests}/requests/{request.GetProperty("id").GetGuid()}/{action}");
        message.Headers.TryAddWithoutValidation("If-Match", request.GetProperty("eTag").GetString());
        return await ReadAsync(await client.SendAsync(message, Ct));
    }

    private static string[] Actions(JsonElement request) => [.. request.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!)];

    // ---- Rung 1 (setting) and rung 3 (strategy) ---------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task MYR_300_is_approved_on_submit_in_demo_and_waits_for_a_manager_in_demo_plus(string provider)
    {
        await using var host = await StartAsync(provider);

        var demo = Client(host, "demo", null, AsUser);
        var plus = Client(host, "demo-plus", null, AsUser);

        var inDemo = await ActAsync(demo, await CreateAsync(demo, NewRequest(300m)), "submit");
        inDemo.GetProperty("status").GetString().ShouldBe("approved");
        inDemo.GetProperty("autoApproved").GetBoolean().ShouldBeTrue();

        var inPlus = await ActAsync(plus, await CreateAsync(plus, NewRequest(300m, "CC-100")), "submit");
        inPlus.GetProperty("status").GetString().ShouldBe("submitted");
        inPlus.GetProperty("autoApproved").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task MYR_6000_needs_one_approval_in_demo_and_two_in_demo_plus(string provider)
    {
        await using var host = await StartAsync(provider);

        // demo: the single-approver default. One manager is enough.
        var demoUser = Client(host, "demo", null, AsUser);
        var demoRequest = await ActAsync(demoUser, await CreateAsync(demoUser, NewRequest(6000m)), "submit");
        var demoManager = Client(host, "demo", null, AsManager);
        (await ActAsync(demoManager, demoRequest, "approve")).GetProperty("status").GetString().ShouldBe("approved");

        // demo-plus: two-level. A manager, then an admin as well, because the total is above MYR 5,000.
        var plusUser = Client(host, "demo-plus", null, AsUser);
        var plusRequest = await ActAsync(plusUser, await CreateAsync(plusUser, NewRequest(6000m, "CC-100")), "submit");
        var manager = Client(host, "demo-plus", null, AsManager);
        var afterManager = await ActAsync(manager, plusRequest, "approve");
        afterManager.GetProperty("status").GetString().ShouldBe("submitted");

        var secondManager = Client(host, "demo-plus", null, AsManager);
        Actions(await ReadAsync(await secondManager.GetAsync($"{Requests}/requests/{plusRequest.GetProperty("id").GetGuid()}", Ct)))
            .ShouldBe(["reject"], "a second manager can't give the admin step");

        var admin = Client(host, "demo-plus", null, AsAdmin);
        (await ActAsync(admin, afterManager, "approve")).GetProperty("status").GetString().ShouldBe("approved");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Below_the_threshold_two_level_needs_only_the_manager(string provider)
    {
        await using var host = await StartAsync(provider);
        var user = Client(host, "demo-plus", null, AsUser);
        var request = await ActAsync(user, await CreateAsync(user, NewRequest(1000m, "CC-100")), "submit");
        var manager = Client(host, "demo-plus", null, AsManager);
        (await ActAsync(manager, request, "approve")).GetProperty("status").GetString().ShouldBe("approved");
    }

    // ---- Rung 4 (custom fields) ----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Cost_center_is_required_only_in_demo_plus_and_the_server_checks_the_value(string provider)
    {
        await using var host = await StartAsync(provider);

        // demo has no custom fields: none is required, and an unknown one is refused rather than silently stored.
        var demo = Client(host, "demo", null, AsUser);
        (await CreateAsync(demo, NewRequest(100m))).GetProperty("customFields").EnumerateObject().ShouldBeEmpty();
        var unknown = await demo.PostAsJsonAsync($"{Requests}/requests", NewRequest(100m, "CC-100"), Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var plus = Client(host, "demo-plus", null, AsUser);
        var missing = await plus.PostAsJsonAsync($"{Requests}/requests", NewRequest(100m), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(missing, HttpStatusCode.BadRequest)).GetProperty("errors").EnumerateObject()
            .Select(e => e.Name).ShouldContain("customFields.cost-center");

        var wrong = await plus.PostAsJsonAsync($"{Requests}/requests", NewRequest(100m, "CC-999"), Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await CreateAsync(plus, NewRequest(100m, "CC-200"));
        ok.GetProperty("customFields").GetProperty(WellKnownCustomFields.CostCenter).GetString().ShouldBe("CC-200");

        // The definitions API returns what the form renders.
        var definitions = await ReadAsync(await plus.GetAsync($"{Requests}/custom-fields?entity={Modules.PurchaseRequest}", Ct));
        definitions.GetArrayLength().ShouldBe(1);
        definitions[0].GetProperty("options").EnumerateArray().Select(o => o.GetString()).ShouldBe(["CC-100", "CC-200"]);
        var none = await ReadAsync(await demo.GetAsync($"{Requests}/custom-fields?entity={Modules.PurchaseRequest}", Ct));
        none.GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Only_admins_define_custom_fields(string provider)
    {
        await using var host = await StartAsync(provider);
        var manager = Client(host, "demo", null, AsManager);
        var denied = await manager.PutAsJsonAsync($"{Requests}/custom-fields/{Modules.PurchaseRequest}/reference", new { label = "Reference", type = "text", required = false, displayOrder = 1 }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    // ---- Rung 2 (feature flag) ----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Draft_sharing_does_not_exist_in_demo_and_works_in_demo_plus(string provider)
    {
        await using var host = await StartAsync(provider);

        var demo = Client(host, "demo", null, AsUser);
        var draft = await CreateAsync(demo, NewRequest(100m));
        var off = await demo.PostAsync($"{Requests}/requests/{draft.GetProperty("id").GetGuid()}/share-draft", null, Ct);
        off.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var plus = Client(host, "demo-plus", null, AsUser);
        var own = await CreateAsync(plus, NewRequest(100m, "CC-100"));
        var on = await ReadAsync(await plus.PostAsync($"{Requests}/requests/{own.GetProperty("id").GetGuid()}/share-draft", null, Ct));
        on.GetProperty("url").GetString().ShouldBe($"/{Modules.PurchaseRequest}/{own.GetProperty("id").GetGuid()}");
    }

    // ---- Rung 7 (tenant-only module) -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Cost_center_budget_is_refused_for_demo(string provider)
    {
        await using var host = await StartAsync(provider);
        var audience = CostCenterBudgetModule.Service;
        using var demo = host.Client("demo", host.TokenFor(audience, "demo", null, PrincipalType.User, AsFinance));
        var response = await demo.GetAsync($"{Budgets}/budgets", Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync(response, HttpStatusCode.Forbidden)).GetProperty("code").GetString().ShouldBe("platform.module-not-entitled");
    }

    // ---- Rung 5 (event extension) ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Approving_in_demo_plus_uses_the_budget_exactly_once_even_when_the_event_arrives_twice(string provider)
    {
        await using var host = await StartAsync(provider);
        using var finance = host.Client("demo-plus", host.TokenFor(CostCenterBudgetModule.Service, "demo-plus", null, PrincipalType.User, AsFinance));
        var budget = await ReadAsync(await finance.PutAsJsonAsync($"{Budgets}/budgets/CC-100", new { name = "Facilities", amount = 10_000m }, Ct));
        budget.GetProperty("remaining").GetDecimal().ShouldBe(10_000m);

        var user = Client(host, "demo-plus", null, AsUser);
        var manager = Client(host, "demo-plus", null, AsManager);
        var submitted = await ActAsync(user, await CreateAsync(user, NewRequest(1500m, "CC-100")), "submit");
        var approved = await ActAsync(manager, submitted, "approve");
        approved.GetProperty("status").GetString().ShouldBe("approved");

        await WaitForRemainingAsync(finance, "CC-100", 8_500m);

        // The broker delivers it again (same event ID), and a re-publication with a new event ID for the same request:
        // the inbox and the unique usage row each keep it to one.
        var requestId = approved.GetProperty("id").GetGuid();
        var duplicate = new PurchaseRequestApproved.V1(requestId, approved.GetProperty("number").GetString()!, Guid.NewGuid(), Guid.NewGuid(), 1500m, "MYR", new DateOnly(2026, 10, 9), false, "CC-100");
        var sameEventId = Guid.NewGuid();
        await EventSender.SendAsync(databases, host.Tenants["demo-plus"].Id, duplicate, sameEventId, Ct);
        await EventSender.SendAsync(databases, host.Tenants["demo-plus"].Id, duplicate, sameEventId, Ct);
        await EventSender.SendAsync(databases, host.Tenants["demo-plus"].Id, duplicate, Guid.NewGuid(), Ct);
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);

        (await ReadAsync(await finance.GetAsync($"{Budgets}/budgets/CC-100", Ct))).GetProperty("remaining").GetDecimal().ShouldBe(8_500m);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Finance_lists_and_changes_budgets_with_the_version_it_saw(string provider)
    {
        await using var host = await StartAsync(provider);
        using var finance = host.Client("demo-plus", host.TokenFor(CostCenterBudgetModule.Service, "demo-plus", null, PrincipalType.User, AsFinance));
        await finance.PutAsJsonAsync($"{Budgets}/budgets/CC-100", new { name = "Facilities", amount = 1_000m }, Ct);
        await finance.PutAsJsonAsync($"{Budgets}/budgets/CC-200", new { name = "Marketing", amount = 2_000m }, Ct);

        var list = await ReadAsync(await finance.GetAsync($"{Budgets}/budgets", Ct));
        list.EnumerateArray().Select(b => b.GetProperty("code").GetString()).ShouldBe(["CC-100", "CC-200"]);
        var stale = list[0].GetProperty("eTag").GetString();

        // A change with the version it saw works; the same version again is stale (412), and a missing one is refused (428).
        using var change = new HttpRequestMessage(HttpMethod.Put, $"{Budgets}/budgets/CC-100") { Content = JsonContent.Create(new { name = "Facilities", amount = 1_500m }) };
        change.Headers.TryAddWithoutValidation("If-Match", stale);
        (await ReadAsync(await finance.SendAsync(change, Ct))).GetProperty("amount").GetDecimal().ShouldBe(1_500m);
        using var again = new HttpRequestMessage(HttpMethod.Put, $"{Budgets}/budgets/CC-100") { Content = JsonContent.Create(new { name = "Facilities", amount = 1_800m }) };
        again.Headers.TryAddWithoutValidation("If-Match", stale);
        (await finance.SendAsync(again, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await finance.PutAsJsonAsync($"{Budgets}/budgets/CC-100", new { name = "Facilities", amount = 1_800m }, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        // Users in the module (not finance) can read one budget but not list or change.
        using var user = host.Client("demo-plus", host.TokenFor(CostCenterBudgetModule.Service, "demo-plus", null, PrincipalType.User, [(Modules.CostCenterBudget, Role.User)]));
        (await user.GetAsync($"{Budgets}/budgets/CC-100", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await user.GetAsync($"{Budgets}/budgets", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_request_for_a_cost_center_without_a_budget_uses_nothing(string provider)
    {
        await using var host = await StartAsync(provider);
        using var finance = host.Client("demo-plus", host.TokenFor(CostCenterBudgetModule.Service, "demo-plus", null, PrincipalType.User, AsFinance));
        await finance.PutAsJsonAsync($"{Budgets}/budgets/CC-100", new { name = "Facilities", amount = 1_000m }, Ct);

        var user = Client(host, "demo-plus", null, AsUser);
        var manager = Client(host, "demo-plus", null, AsManager);
        await ActAsync(manager, await ActAsync(user, await CreateAsync(user, NewRequest(300m, "CC-200")), "submit"), "approve");
        await Task.Delay(TimeSpan.FromSeconds(4), Ct);

        (await ReadAsync(await finance.GetAsync($"{Budgets}/budgets/CC-100", Ct))).GetProperty("remaining").GetDecimal().ShouldBe(1_000m);
    }

    private static async Task WaitForRemainingAsync(HttpClient finance, string code, decimal expected)
    {
        for (var i = 0; i < 100; i++)
        {
            var budget = await ReadAsync(await finance.GetAsync($"{Budgets}/budgets/{code}", Ct));
            if (budget.GetProperty("remaining").GetDecimal() == expected)
                return;
            await Task.Delay(300, Ct);
        }
        throw new TimeoutException($"The budget {code} never reached {expected}.");
    }

    // ---- Server-driven actions ----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Allowed_actions_follow_the_role_the_state_and_the_policy(string provider)
    {
        await using var host = await StartAsync(provider);
        var ownerId = Guid.NewGuid();
        var owner = Client(host, "demo-plus", ownerId, AsUser);
        var draft = await CreateAsync(owner, NewRequest(6000m, "CC-100"));
        Actions(draft).ShouldBe(["edit", "delete", "submit", "uploadQuote"], ignoreOrder: true);

        var submitted = await ActAsync(owner, draft, "submit");
        Actions(submitted).ShouldBeEmpty("the owner can't approve their own request");

        var manager = Client(host, "demo-plus", Guid.NewGuid(), AsManager);
        var asManager = await ReadAsync(await manager.GetAsync($"{Requests}/requests/{submitted.GetProperty("id").GetGuid()}", Ct));
        Actions(asManager).ShouldBe(["approve", "reject"], ignoreOrder: true);

        var afterFirst = await ActAsync(manager, asManager, "approve");
        Actions(afterFirst).ShouldBe(["reject"], "this manager already gave the first approval, and the second step needs an admin");

        var admin = Client(host, "demo-plus", Guid.NewGuid(), AsAdmin);
        Actions(await ReadAsync(await admin.GetAsync($"{Requests}/requests/{submitted.GetProperty("id").GetGuid()}", Ct))).ShouldBe(["approve", "reject"], ignoreOrder: true);
    }

    // ---- Tenant isolation of customization ---------------------------------------------------------

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task One_tenants_customization_never_affects_another(string provider)
    {
        await using var host = await StartAsync(provider);
        // demo-plus's custom field, setting and flag are invisible to demo (checked above); here: a stored setting is per tenant.
        await host.InTenantAsync("demo", async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogDbContext>();
            (await catalog.TenantSettings.CountAsync(s => s.TenantId == host.Tenants["demo"].Id, Ct)).ShouldBe(0);
            (await catalog.TenantFeatures.CountAsync(f => f.TenantId == host.Tenants["demo"].Id, Ct)).ShouldBe(0);
        });
    }
}
