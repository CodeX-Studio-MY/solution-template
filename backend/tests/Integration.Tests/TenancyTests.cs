using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Farm360.Core.Catalog;
using Farm360.Core.Platform;
using Farm360.Core.Tenancy;
using Farm360.Identity.Infrastructure;
using Farm360.Tenancy.Application;
using Farm360.Tenancy.Infrastructure;
using Farm360.Migrator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Farm360.Integration.Tests;

[Trait("service", "tenancy")]
public sealed class TenancyTests(Databases databases)
{
    private const string Api = "/api/tenancy/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    /// <summary>Creates an operator, signs in with the password and a current TOTP code, and returns an authenticated client.</summary>
    private static async Task<HttpClient> OperatorAsync(TenancyTestHost host, string email = "ops@example.com")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync(email, "Ops One", Ct);
        var client = host.Client();
        var code = Totp.Compute(created.TotpSecret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30);
        var session = await Read<SessionDto>(await client.PostAsJsonAsync($"{Api}/operator/sessions", new { email, password = created.Password, totpCode = code }, Ct));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Operator {session.Token}");
        return client;
    }

    private static Task<HttpResponseMessage> OnboardAsync(HttpClient op, string slug, string? plan = null) =>
        op.PostAsJsonAsync($"{Api}/operator/tenants", new { slug, displayName = slug.ToUpperInvariant(), plan = plan ?? Fixture.BasePlan, adminEmail = $"admin@{slug}.example.com", adminName = "Admin" }, Ct);

    [Fact]
    public async Task Operators_need_the_password_and_a_valid_TOTP_code_and_failures_look_alike()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        await using var scope = host.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider.GetRequiredService<IOperatorAccounts>().CreateAsync("ops@example.com", "Ops", Ct);
        var client = host.Client();
        var good = Totp.Compute(created.TotpSecret, TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() / 30);

        var wrongCode = await client.PostAsJsonAsync($"{Api}/operator/sessions", new { email = "ops@example.com", password = created.Password, totpCode = "000000" }, Ct);
        var wrongPassword = await client.PostAsJsonAsync($"{Api}/operator/sessions", new { email = "ops@example.com", password = "nope", totpCode = good }, Ct);
        var unknown = await client.PostAsJsonAsync($"{Api}/operator/sessions", new { email = "nobody@example.com", password = "x", totpCode = good }, Ct);
        foreach (var response in new[] { wrongCode, wrongPassword, unknown })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("tenant-settings.operator-login-failed");
        }

        // Anonymous callers, and tenant bearer tokens, can't use the operator API.
        (await client.GetAsync($"{Api}/operator/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var tenantCaller = host.Client();
        tenantCaller.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(Guid.NewGuid(), roles: ["tenant-settings:admin"], audience: PlatformServices.Tenancy.Audience));
        (await tenantCaller.GetAsync($"{Api}/operator/tenants", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Onboarding_creates_an_isolated_ready_tenant_and_invites_the_first_admin()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);

        var response = await OnboardAsync(op, "farm360-co");
        var result = await Read<JsonElement>(response);
        var tenantId = result.GetProperty("tenantId").GetGuid();
        var invitation = result.GetProperty("adminInvitationUrl").GetString()!;
        invitation.ShouldStartWith("https://login.localhost/account/invitation?tenant=farm360-co&token=");

        await using var scope = host.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await catalog.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, Ct);
        tenant.Status.ShouldBe(TenantStatus.Active);
        tenant.Placement.DatabaseName.ShouldBe("farm360_farm360_co");
        tenant.Placement.Provider.ShouldBe(Databases.DefaultProvider);
        (await catalog.TenantModules.Where(t => t.TenantId == tenantId).CountAsync(Ct)).ShouldBe(Modules.All.Count(m => m.Plans.Contains(Fixture.BasePlan)));

        // The tenant's database exists and is migrated: its first admin can accept the invitation.
        using var browser = host.Identity.Browser();
        var page = await browser.GetAsync(new Uri(invitation).PathAndQuery);
        page.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Onboarding_is_idempotent_and_resumes_after_a_failure()
    {
        await using var host = await TenancyTestHost.StartAsync(databases, builder =>
        {
            builder.Services.AddSingleton<FlakyMigrator>();
            builder.Services.AddScoped<ITenantMigrator>(sp => sp.GetRequiredService<FlakyMigrator>());
        });
        var op = await OperatorAsync(host);

        var first = await OnboardAsync(op, "resumable");
        first.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("tenant-settings.onboarding-failed");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>().OnboardingRuns.AsNoTracking().SingleAsync(r => r.Slug == "resumable", Ct);
            run.NextStep.ShouldBe(Farm360.Tenancy.Domain.OnboardingStep.Migrations);
            run.Error.ShouldNotBeNull();
            (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.CountAsync(t => t.Slug == "resumable", Ct)).ShouldBe(1);
        }

        // Running it again resumes at the failed step and finishes; nothing is duplicated.
        var second = await Read<JsonElement>(await OnboardAsync(op, "resumable"));
        second.GetProperty("adminInvitationUrl").GetString().ShouldNotBeNull();
        var third = await OnboardAsync(op, "resumable");
        (await Read<JsonElement>(third)).GetProperty("tenantId").GetGuid().ShouldBe(second.GetProperty("tenantId").GetGuid());

        await using var verify = host.Services.CreateAsyncScope();
        (await verify.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.CountAsync(t => t.Slug == "resumable", Ct)).ShouldBe(1);
        host.Services.GetRequiredService<FlakyMigrator>().Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData("Bad_Slug")]
    [InlineData("x")]
    [InlineData("login")]
    [InlineData("operator")]
    public async Task Invalid_or_reserved_slugs_are_refused(string slug)
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        (await OnboardAsync(op, slug)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_lifecycle_moves_through_suspended_and_pending_deletion_to_deleted()
    {
        await using var host = await TenancyTestHost.StartAsync(databases, builder => builder.Configuration["Onboarding:DeletionGraceDays"] = "0");
        var op = await OperatorAsync(host);
        var tenantId = (await Read<JsonElement>(await OnboardAsync(op, "doomed"))).GetProperty("tenantId").GetGuid();

        (await Read<JsonElement>(await op.PostAsync($"{Api}/operator/tenants/{tenantId}/suspend", null, Ct))).GetProperty("status").GetString().ShouldBe("suspended");
        (await op.PostAsync($"{Api}/operator/tenants/{tenantId}/suspend", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);   // not Active any more
        (await Read<JsonElement>(await op.PostAsync($"{Api}/operator/tenants/{tenantId}/resume", null, Ct))).GetProperty("status").GetString().ShouldBe("active");

        // Complete deletion is refused until offboarding began and the grace period passed.
        (await op.PostAsync($"{Api}/operator/tenants/{tenantId}/complete-deletion", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var offboarding = await Read<JsonElement>(await op.PostAsync($"{Api}/operator/tenants/{tenantId}/offboard", null, Ct));
        offboarding.GetProperty("status").GetString().ShouldBe("pendingDeletion");
        offboarding.GetProperty("deleteAfter").ValueKind.ShouldNotBe(JsonValueKind.Null);

        // Cancelling during the grace period restores the tenant.
        (await Read<JsonElement>(await op.PostAsync($"{Api}/operator/tenants/{tenantId}/cancel-offboarding", null, Ct))).GetProperty("status").GetString().ShouldBe("active");

        await op.PostAsync($"{Api}/operator/tenants/{tenantId}/offboard", null, Ct);
        string database;
        await using (var scope = host.Services.CreateAsyncScope())
            database = (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId, Ct)).Placement.DatabaseName;
        (await DatabaseExistsAsync(database)).ShouldBeTrue();

        (await Read<JsonElement>(await op.PostAsync($"{Api}/operator/tenants/{tenantId}/complete-deletion", null, Ct))).GetProperty("status").GetString().ShouldBe("deleted");
        (await DatabaseExistsAsync(database)).ShouldBeFalse();
        (await op.PostAsync($"{Api}/operator/tenants/{tenantId}/resume", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);   // deleted is final
    }

    [Fact]
    public async Task Changing_the_plan_changes_the_tenants_modules_immediately()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = (await Read<JsonElement>(await OnboardAsync(op, "grower"))).GetProperty("tenantId").GetGuid();

        async Task<string[]> ModulesSeenByTenantAsync()
        {
            var caller = host.Client("grower.localhost");
            caller.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenantId, roles: [], audience: PlatformServices.Tenancy.Audience));
            var manifest = await Read<JsonElement>(await caller.GetAsync($"{Api}/me/modules", Ct));
            return [.. manifest.GetProperty("modules").EnumerateArray().Select(m => m.GetProperty("id").GetString()!)];
        }

        // What platform.yaml says each plan includes. The manifest lists only the modules with a frontend: those a service of the
        // same key owns (the platform's own modules have their screens in the shell).
        static string[] ModulesOf(string plan) => [.. Modules.All.Where(m => m.Plans.Contains(plan)).Select(m => m.Key).Order(StringComparer.Ordinal)];
        static string[] ManifestOf(string plan) =>
            [.. ModulesOf(plan).Where(key => PlatformServices.All.Any(s => s.Key == key && s.OwnedModules.Contains(key)))];

        (await ModulesSeenByTenantAsync()).Order(StringComparer.Ordinal).ShouldBe(ManifestOf(Fixture.BasePlan));

        var upgraded = await Read<JsonElement>(await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/plan", new { plan = Fixture.TopPlan }, Ct));
        upgraded.GetProperty("modules").EnumerateArray().Select(m => m.GetString()!).Order(StringComparer.Ordinal).ShouldBe(ModulesOf(Fixture.TopPlan));
        (await ModulesSeenByTenantAsync()).Order(StringComparer.Ordinal).ShouldBe(ManifestOf(Fixture.TopPlan));

        await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/plan", new { plan = Fixture.BasePlan }, Ct);
        (await ModulesSeenByTenantAsync()).Order(StringComparer.Ordinal).ShouldBe(ManifestOf(Fixture.BasePlan));
        (await op.PutAsJsonAsync($"{Api}/operator/tenants/{tenantId}/plan", new { plan = "not-a-plan" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The manifest carries the module key and a URL built from configuration and the catalog's version. A product with business
        // modules checks this; the template has none yet.
        var caller = host.Client("grower.localhost");
        caller.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenantId, roles: [], audience: PlatformServices.Tenancy.Audience));
        foreach (var entry in (await Read<JsonElement>(await caller.GetAsync($"{Api}/me/modules", Ct))).GetProperty("modules").EnumerateArray())
            entry.GetProperty("entry").GetString().ShouldBe($"/mfe/{entry.GetProperty("id").GetString()}/0.0.0-dev/mf-manifest.json");
    }

    [Fact]
    public async Task Support_access_is_restricted_time_limited_approved_by_the_tenant_and_attributed_to_the_operator()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenantId = (await Read<JsonElement>(await OnboardAsync(op, "helped"))).GetProperty("tenantId").GetGuid();
        var admin = host.Client("helped.localhost");
        admin.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenantId, roles: ["tenant-settings:admin"], audience: PlatformServices.Tenancy.Audience));

        // Operators never get more than manager, and need a real reason.
        (await op.PostAsJsonAsync($"{Api}/operator/support-access", new { tenantSlug = "helped", reason = "Investigating ticket 42 for the customer", role = "admin", module = Modules.TenantSettings, minutes = 30 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await op.PostAsJsonAsync($"{Api}/operator/support-access", new { tenantSlug = "helped", reason = "short", role = "user", module = Modules.TenantSettings, minutes = 30 }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var requested = await Read<JsonElement>(await op.PostAsJsonAsync($"{Api}/operator/support-access", new { tenantSlug = "helped", reason = "Investigating ticket 42 for the customer", role = "manager", module = Modules.TenantSettings, minutes = 30 }, Ct));
        var grantId = requested.GetProperty("id").GetGuid();
        requested.GetProperty("status").GetString().ShouldBe("pending");

        // Not approved yet: no token.
        (await op.PostAsync($"{Api}/operator/support-access/{grantId}/token", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The tenant admin sees the request and approves it.
        var listed = await Read<JsonElement[]>(await admin.GetAsync($"{Api}/support-access", Ct));
        listed.Single().GetProperty("reason").GetString()!.ShouldContain("ticket 42");
        (await admin.PostAsync($"{Api}/support-access/{grantId}/approve", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var issued = await Read<JsonElement>(await op.PostAsync($"{Api}/operator/support-access/{grantId}/token", null, Ct));
        var token = new JsonWebTokenHandler().ReadJsonWebToken(issued.GetProperty("accessToken").GetString());
        token.GetClaim("tenant_id").Value.ShouldBe(tenantId.ToString());
        token.GetClaim("roles").Value.ShouldBe("tenant-settings:manager");
        token.Audiences.ShouldBe([PlatformServices.Tenancy.Audience]);   // only the service that owns the module
        using var act = JsonDocument.Parse(token.GetClaim("act").Value);
        act.RootElement.GetProperty("sub").GetString().ShouldBe(requested.GetProperty("operatorId").GetGuid().ToString());
        (token.ValidTo - token.IssuedAt).TotalMinutes.ShouldBeLessThanOrEqualTo(10);

        // The tenant sees it in its own audit log, attributed to the operator.
        await host.Identity.InTenantAsync("helped", async sp =>
        {
            var entry = await sp.GetRequiredService<IdentityDbContext>().AuditLog.SingleAsync(a => a.Kind == "support-access", Ct);
            entry.ActorType.ShouldBe("operator");
            entry.ActorId.ShouldBe(requested.GetProperty("operatorId").GetGuid().ToString());
        });

        // Revoking takes effect at the next token request.
        (await admin.PostAsync($"{Api}/support-access/{grantId}/revoke", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await op.PostAsync($"{Api}/operator/support-access/{grantId}/token", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_tenant_can_waive_approval_and_other_tenants_admins_cannot_decide_its_grants()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var one = (await Read<JsonElement>(await OnboardAsync(op, "tenant-one"))).GetProperty("tenantId").GetGuid();
        await OnboardAsync(op, "tenant-two");
        await op.PutAsJsonAsync($"{Api}/operator/tenants/{one}/support-approval", new { required = false }, Ct);

        var auto = await Read<JsonElement>(await op.PostAsJsonAsync($"{Api}/operator/support-access", new { tenantSlug = "tenant-one", reason = "Routine health check requested", role = "user", module = Modules.TenantSettings, minutes = 15 }, Ct));
        auto.GetProperty("status").GetString().ShouldBe("approved");

        var pending = await Read<JsonElement>(await op.PostAsJsonAsync($"{Api}/operator/support-access", new { tenantSlug = "tenant-two", reason = "Routine health check requested", role = "user", module = Modules.TenantSettings, minutes = 15 }, Ct));
        pending.GetProperty("status").GetString().ShouldBe("pending");

        // An admin of tenant-one can't approve tenant-two's grant.
        var otherAdmin = host.Client("tenant-one.localhost");
        otherAdmin.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(one, roles: ["tenant-settings:admin"], audience: PlatformServices.Tenancy.Audience));
        (await otherAdmin.PostAsync($"{Api}/support-access/{pending.GetProperty("id").GetGuid()}/approve", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await op.PostAsync($"{Api}/operator/support-access/{pending.GetProperty("id").GetGuid()}/token", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private async Task<bool> DatabaseExistsAsync(string name)
    {
        // Provider-neutral: EF reports a missing database as "cannot connect".
        var provider = Databases.Provider(Databases.DefaultProvider);
        var connectionString = provider.BuildConnectionString(databases.ServerConnectionString(Databases.DefaultProvider), name, 1);
        await using var db = MigrationTargets.Tenant[0].Create(provider, connectionString);
        return await db.Database.CanConnectAsync(Ct);
    }

    /// <summary>A migrator that fails on its first call and then delegates to the real in-process one.</summary>
    private sealed class FlakyMigrator(IServiceProvider services, Farm360.Core.Tenancy.ITenantDirectory directory) : ITenantMigrator
    {
        public int Calls { get; private set; }

        public async Task MigrateAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Calls == 1)
                throw new InvalidOperationException("the migrator job could not start");
            var runner = ActivatorUtilities.CreateInstance<Farm360.Migrator.MigrationRunner>(services);
            var tenant = await directory.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException("Unknown tenant.");
            if (!await runner.MigrateTenantAsync(tenant, cancellationToken))
                throw new InvalidOperationException("The migrator failed.");
        }
    }
}
