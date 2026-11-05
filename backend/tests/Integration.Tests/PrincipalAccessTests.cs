using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Tenancy.Application;
using Saaskit.Tenancy.Domain;
using Saaskit.Tenancy.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Saaskit.Integration.Tests;

/// <summary>The catalog's users, devices and applications, and which tenants each may access, through the operator API.</summary>
[Trait("service", "tenancy")]
public sealed class PrincipalAccessTests(Databases databases)
{
    private const string Api = "/api/tenancy/v1/operator";
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
        var session = await Read(await client.PostAsJsonAsync($"{Api}/sessions", new { email = "ops@example.com", password = created.Password, totpCode = code }, Ct));
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Operator {session.GetProperty("token").GetString()}");
        return client;
    }

    private static async Task<Guid> TenantAsync(HttpClient op, string slug) =>
        (await Read(await op.PostAsJsonAsync($"{Api}/tenants", new { slug, displayName = slug, plan = Fixture.BasePlan, adminEmail = $"a@{slug}.example.com", adminName = "A" }, Ct), HttpStatusCode.Created))
            .GetProperty("tenantId").GetGuid();

    private static async Task<Guid> PrincipalAsync(HttpClient op, object body) =>
        (await Read(await op.PostAsJsonAsync($"{Api}/principals", body, Ct), HttpStatusCode.Created)).GetProperty("principalId").GetGuid();

    private static object User(string email) => new { type = "user", displayName = "Aisha", email };

    private static object Device(string identifier) => new { type = "device", displayName = "North Gate", deviceType = "lorawanGateway", identifier };

    [Fact]
    public async Task One_principal_reaches_two_tenants_and_every_change_is_audited()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var first = await TenantAsync(op, "first");
        var second = await TenantAsync(op, "second");
        var id = await PrincipalAsync(op, User("Aisha@Example.com"));

        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{first}/principals/{id}", new { reason = "New starter" }, Ct));
        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{second}/principals/{id}", new { }, Ct));
        foreach (var tenant in new[] { first, second })
        {
            var rows = (await Read(await op.GetAsync($"{Api}/tenants/{tenant}/principals", Ct))).GetProperty("items");
            rows.GetArrayLength().ShouldBe(1);
            rows[0].GetProperty("principalId").GetGuid().ShouldBe(id);
            rows[0].GetProperty("identifier").GetString().ShouldBe("aisha@example.com");
            rows[0].GetProperty("grantedByName").GetString().ShouldBe("Ops One");
        }

        // Granting what is already active changes nothing, so it adds no audit row.
        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{first}/principals/{id}", new { }, Ct));

        async Task Move(string status, string reason) =>
            await Read(await op.PutAsJsonAsync($"{Api}/tenants/{first}/principals/{id}/status", new { status, reason }, Ct));
        await Move("suspended", "On leave");
        await Move("active", "Back");
        await Move("revoked", "Left the company");

        // A revoked row stays one row, and only a new grant brings it back.
        (await op.PutAsJsonAsync($"{Api}/tenants/{first}/principals/{id}/status", new { status = "active", reason = "No" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await Read(await op.PutAsJsonAsync($"{Api}/tenants/{first}/principals/{id}", new { }, Ct))).GetProperty("status").GetString().ShouldBe("active");

        var trail = (await Read(await op.GetAsync($"{Api}/principals/{id}/access-audit?tenantId={first}", Ct))).GetProperty("items").EnumerateArray().ToList();
        trail.Select(a => a.GetProperty("action").GetString()).ShouldBe(["granted", "revoked", "resumed", "suspended", "granted"]);
        trail[0].GetProperty("fromStatus").GetString().ShouldBe("revoked");
        trail[1].GetProperty("reason").GetString().ShouldBe("Left the company");
        trail[1].GetProperty("toStatus").GetString().ShouldBe("revoked");
        trail[1].GetProperty("actorName").GetString().ShouldBe("Ops One");
        trail[4].GetProperty("fromStatus").ValueKind.ShouldBe(JsonValueKind.Null);
        trail[4].GetProperty("reason").GetString().ShouldBe("New starter");
        (await Read(await op.GetAsync($"{Api}/tenants/{second}/principal-access-audit", Ct))).GetProperty("items").GetArrayLength().ShouldBe(1);

        var detail = await Read(await op.GetAsync($"{Api}/principals/{id}", Ct));
        detail.GetProperty("tenants").GetArrayLength().ShouldBe(2);

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        (await db.TenantPrincipalAccess.CountAsync(a => a.PrincipalId == id && a.TenantId == first, Ct)).ShouldBe(1);
        (await db.Principals.SingleAsync(p => p.Id == id, Ct)).CreatedBy.ShouldNotBe("system");   // the operator, not a background job
    }

    [Fact]
    public async Task Bad_input_duplicates_and_stale_edits_are_refused()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenant = await TenantAsync(op, "rules");
        var id = await PrincipalAsync(op, User("sam@example.com"));
        var device = await PrincipalAsync(op, Device("ac1f09fffe0512a4"));

        // The same email in any case, and the same device identifier in any case.
        (await op.PostAsJsonAsync($"{Api}/principals", User("SAM@example.com"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await op.PostAsJsonAsync($"{Api}/principals", Device("AC1F09FFFE0512A4"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        // The same identifier on another kind of device is a different device.
        await PrincipalAsync(op, new { type = "device", displayName = "Sensor", deviceType = "iot", identifier = "AC1F09FFFE0512A4" });

        var bad = await Read(await op.PostAsJsonAsync($"{Api}/principals", new { type = "user", displayName = "", email = "josé@example.com" }, Ct), HttpStatusCode.BadRequest);
        bad.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ShouldBe(["displayName", "email"]);
        var application = await Read(await op.PostAsJsonAsync($"{Api}/principals", new { type = "application", displayName = "ERP" }, Ct), HttpStatusCode.BadRequest);
        application.GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ShouldBe(["isExternal", "ownerName"]);

        // A principal's type never changes.
        (await op.PutAsJsonAsync($"{Api}/principals/{id}", Device("X1"), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{tenant}/principals/{id}", new { }, Ct));
        var noReason = await Read(await op.PutAsJsonAsync($"{Api}/tenants/{tenant}/principals/{id}/status", new { status = "revoked", reason = " " }, Ct), HttpStatusCode.BadRequest);
        noReason.GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldBe(["reason"]);
        (await op.PutAsJsonAsync($"{Api}/tenants/{tenant}/principals/{device}/status", new { status = "revoked", reason = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // An edit made from an old copy is refused; a deactivated principal can't be given access.
        var etag = (await Read(await op.GetAsync($"{Api}/principals/{device}", Ct))).GetProperty("eTag").GetString()!;
        using (var rename = new HttpRequestMessage(HttpMethod.Put, $"{Api}/principals/{device}/active") { Content = JsonContent.Create(new { active = false }) })
        {
            rename.Headers.TryAddWithoutValidation("If-Match", etag);
            (await op.SendAsync(rename, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var stale = new HttpRequestMessage(HttpMethod.Put, $"{Api}/principals/{device}/active") { Content = JsonContent.Create(new { active = true }) })
        {
            stale.Headers.TryAddWithoutValidation("If-Match", etag);
            (await op.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        }

        (await op.PutAsJsonAsync($"{Api}/tenants/{tenant}/principals/{device}", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var found = (await Read(await op.GetAsync($"{Api}/principals?type=device&search=ac1f", Ct))).GetProperty("items");
        found.GetArrayLength().ShouldBe(2);
        found.EnumerateArray().ShouldAllBe(p => p.GetProperty("type").GetString() == "device");
    }

    [Fact]
    public async Task Providers_are_unique_per_account_and_the_logs_are_append_only()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenant = await TenantAsync(op, "logs");
        var one = await PrincipalAsync(op, User("one@example.com"));
        var two = await PrincipalAsync(op, User("two@example.com"));
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        db.PrincipalUserProviders.Add(new PrincipalUserProvider(one, LoginProvider.Google, "g-1", "one@gmail.com", now));
        db.PrincipalUserProviders.Add(new PrincipalUserProvider(one, LoginProvider.Microsoft, "m-1", null, now));
        await db.SaveChangesAsync(Ct);
        var providers = (await Read(await op.GetAsync($"{Api}/principals/{one}", Ct))).GetProperty("providers").EnumerateArray().ToList();
        providers.Select(p => p.GetProperty("provider").GetString()).ShouldBe(["google", "microsoft"]);

        var taken = db.PrincipalUserProviders.Add(new PrincipalUserProvider(two, LoginProvider.Google, "g-1", null, now));
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
        taken.State = EntityState.Detached;

        var login = new TenantPrincipalLogin(now, tenant, one, PlatformPrincipalType.User, true, LoginMethod.Google, null, Guid.NewGuid(), "203.0.113.9", "Test", null);
        db.TenantPrincipalLogins.Add(login);
        await db.SaveChangesAsync(Ct);
        var logins = (await Read(await op.GetAsync($"{Api}/tenants/{tenant}/principal-logins", Ct))).GetProperty("items");
        logins[0].GetProperty("method").GetString().ShouldBe("google");
        logins[0].GetProperty("principalName").GetString().ShouldBe("Aisha");

        db.Entry(login).State = EntityState.Modified;
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Deleting_a_tenant_removes_its_access_and_logins_and_keeps_the_rest()
    {
        await using var host = await TenancyTestHost.StartAsync(databases, builder => builder.Configuration["Onboarding:DeletionGraceDays"] = "0");
        var op = await OperatorAsync(host);
        var doomed = await TenantAsync(op, "gone");
        var kept = await TenantAsync(op, "kept");
        var id = await PrincipalAsync(op, User("both@example.com"));
        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{doomed}/principals/{id}", new { }, Ct));
        await Read(await op.PutAsJsonAsync($"{Api}/tenants/{kept}/principals/{id}", new { }, Ct));

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyCatalogDbContext>();
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        Guid actor = (await db.Operators.AsNoTracking().FirstAsync(Ct)).Id;
        db.TenantPrincipalAccessAudit.Add(new TenantPrincipalAccessAudit(now, doomed, id, TenantAccessAction.Suspended, TenantAccessStatus.Active, TenantAccessStatus.Suspended, actor, "x", "203.0.113.9", null));
        db.TenantPrincipalLogins.Add(new TenantPrincipalLogin(now, doomed, id, PlatformPrincipalType.User, true, LoginMethod.Password, null, null, "203.0.113.9", null, null));
        db.TenantPrincipalLogins.Add(new TenantPrincipalLogin(now, kept, id, PlatformPrincipalType.User, true, LoginMethod.Password, null, null, "203.0.113.9", null, null));
        await db.SaveChangesAsync(Ct);

        await Read(await op.PostAsync($"{Api}/tenants/{doomed}/offboard", null, Ct));
        await Read(await op.PostAsync($"{Api}/tenants/{doomed}/complete-deletion", null, Ct));
        (await op.PutAsJsonAsync($"{Api}/tenants/{doomed}/principals/{id}", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        db.ChangeTracker.Clear();
        (await db.TenantPrincipalAccess.CountAsync(a => a.TenantId == doomed, Ct)).ShouldBe(0);
        (await db.TenantPrincipalLogins.CountAsync(l => l.TenantId == doomed, Ct)).ShouldBe(0);
        var audit = await db.TenantPrincipalAccessAudit.Where(a => a.TenantId == doomed).ToListAsync(Ct);
        audit.Count.ShouldBe(2);
        audit.ShouldAllBe(a => a.RemoteAddress == null);
        (await db.TenantPrincipalAccess.CountAsync(a => a.TenantId == kept, Ct)).ShouldBe(1);
        (await db.TenantPrincipalLogins.CountAsync(l => l.TenantId == kept && l.RemoteAddress != null, Ct)).ShouldBe(1);
        (await db.Principals.CountAsync(p => p.Id == id, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Every_route_needs_an_operator_session()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await OperatorAsync(host);
        var tenant = await TenantAsync(op, "guarded");
        var id = Guid.NewGuid();
        var anonymous = host.Client();
        var tenantUser = host.Client("guarded.localhost");
        tenantUser.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Identity.Callers.Create(tenant, roles: [$"{Modules.TenantSettings}:admin"], audience: PlatformServices.Tenancy.Audience));

        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "principals"),
            (HttpMethod.Post, "principals"),
            (HttpMethod.Get, $"principals/{id}"),
            (HttpMethod.Put, $"principals/{id}"),
            (HttpMethod.Put, $"principals/{id}/active"),
            (HttpMethod.Get, $"principals/{id}/access-audit"),
            (HttpMethod.Get, $"tenants/{tenant}/principals"),
            (HttpMethod.Put, $"tenants/{tenant}/principals/{id}"),
            (HttpMethod.Put, $"tenants/{tenant}/principals/{id}/status"),
            (HttpMethod.Get, $"tenants/{tenant}/principal-access-audit"),
            (HttpMethod.Get, $"tenants/{tenant}/principal-logins"),
        ];
        foreach (var (method, path) in routes)
        {
            foreach (var client in new[] { anonymous, tenantUser })
            {
                using var request = new HttpRequestMessage(method, $"{Api}/{path}");
                if (method != HttpMethod.Get)
                    request.Content = JsonContent.Create(new { });
                (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path}");
            }
        }
    }
}
