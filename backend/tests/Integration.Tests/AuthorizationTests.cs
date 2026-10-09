using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Acme.Core.Catalog.Entities;
using Acme.Core.Platform;
using Acme.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Acme.Integration.Tests;

/// <summary>Token validation, module roles, principal types, entitlements, suspension, access_version and rate limits.</summary>
public sealed class AuthorizationTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string PurchaseRequest(Role role) => RoleExtensions.ToModuleRole(Modules.PurchaseRequest, role);

    [Theory]
    [InlineData(PrincipalType.User)]
    [InlineData(PrincipalType.Machine)]
    [InlineData(PrincipalType.Service)]
    public async Task Every_principal_type_is_authorized_by_its_role(PrincipalType type)
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var demo = host.Tenants["demo"].Id;

        var asUser = host.Client("demo.localhost", host.Tokens.Create(demo, type, [PurchaseRequest(Role.User)]));
        (await asUser.GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await asUser.GetAsync(TestPlatformHost.Path("/probe/manager"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var asAdmin = host.Client("demo.localhost", host.Tokens.Create(demo, type, [PurchaseRequest(Role.Admin)]));
        (await asAdmin.GetAsync(TestPlatformHost.Path("/probe/manager"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var noRole = host.Client("demo.localhost", host.Tokens.Create(demo, type, []));
        (await noRole.GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Permissions_map_to_a_minimum_role()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var demo = host.Tenants["demo"].Id;
        (await host.Client("demo.localhost", host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)])).GetAsync(TestPlatformHost.Path("/probe/approve"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await host.Client("demo.localhost", host.Tokens.Create(demo, roles: [PurchaseRequest(Role.Manager)])).GetAsync(TestPlatformHost.Path("/probe/approve"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Principal_type_restrictions_apply()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var demo = host.Tenants["demo"].Id;
        var roles = new[] { PurchaseRequest(Role.Admin) };

        var user = await host.Client("demo.localhost", host.Tokens.Create(demo, PrincipalType.User, roles)).GetAsync(TestPlatformHost.Path("/probe/machine"), Ct);
        user.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(user)).ShouldBe("platform.principal-type-not-allowed");

        (await host.Client("demo.localhost", host.Tokens.Create(demo, PrincipalType.Machine, roles)).GetAsync(TestPlatformHost.Path("/probe/machine"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Entitlement_overrides_the_token()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        string[] roles = [RoleExtensions.ToModuleRole(Modules.CostCenterBudget, Role.Admin)];

        var standard = await host.Client("demo.localhost", host.Tokens.Create(host.Tenants["demo"].Id, roles: roles)).GetAsync(TestPlatformHost.Path("/probe/budget"), Ct);
        standard.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(standard)).ShouldBe("platform.module-not-entitled");

        (await host.Client("demo-plus.localhost", host.Tokens.Create(host.Tenants["demo-plus"].Id, roles: roles)).GetAsync(TestPlatformHost.Path("/probe/budget"), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspended_tenants_can_read_but_not_write()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var client = host.Client("paused.localhost", host.Tokens.Create(host.Tenants["paused"].Id, roles: [PurchaseRequest(Role.Admin)]));

        (await client.GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var write = await client.PostAsync(TestPlatformHost.Path("/probe/user"), null, Ct);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(write)).ShouldBe("platform.tenant-read-only");
    }

    public static TheoryData<string> RejectedTokens() =>
    [
        "tampered-roles", "alg-none", "hmac", "expired", "wrong-audience", "wrong-typ", "other-issuer-key",
    ];

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task Invalid_tokens_are_rejected(string kind)
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var demo = host.Tenants["demo"].Id;
        var valid = host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)]);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var token = kind switch
        {
            "tampered-roles" => TestTokens.Tamper(valid, p => p.Replace(":user", ":admin", StringComparison.Ordinal)),
            "alg-none" => TestTokens.Unsigned(valid),
            "hmac" => host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)],
                credentials: new SigningCredentials(new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256)),
            "expired" => host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)], expires: TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-2)),
            "wrong-audience" => host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)], audience: PlatformServices.CostCenterBudget.Audience),
            "wrong-typ" => host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)], tokenType: "JWT"),
            "other-issuer-key" => host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)],
                credentials: new SigningCredentials(new ECDsaSecurityKey(otherKey) { KeyId = "test-key" }, SecurityAlgorithms.EcdsaSha256)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        (await host.Client("demo.localhost", token).GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.Client("demo.localhost", valid).GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_for_another_tenant_is_refused_on_this_subdomain()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var token = host.Tokens.Create(host.Tenants["demo"].Id, roles: [PurchaseRequest(Role.Admin)]);
        var response = await host.Client("demo-plus.localhost", token).GetAsync(TestPlatformHost.Path("/probe/user"), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Code(response)).ShouldBe("platform.tenant-mismatch");
    }

    [Fact]
    public async Task X_Tenant_works_only_for_platform_services()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var platform = host.Tokens.Create(null, PrincipalType.Service, [PurchaseRequest(Role.User)], permissions: [PlatformPermissions.TenantAny]);
        var client = host.Client("localhost", platform);
        client.DefaultRequestHeaders.Add(PlatformHeaders.Tenant, "demo-plus");
        (await client.GetStringAsync(TestPlatformHost.Path("/probe/whoami"), Ct)).ShouldBe("demo-plus");

        var user = host.Client("localhost", host.Tokens.Create(host.Tenants["demo"].Id, roles: [PurchaseRequest(Role.User)]));
        user.DefaultRequestHeaders.Add(PlatformHeaders.Tenant, "demo-plus");
        (await user.GetAsync(TestPlatformHost.Path("/probe/whoami"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_stale_access_version_returns_401()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var demo = host.Tenants["demo"].Id;
        var subject = Guid.NewGuid().ToString();
        await host.Services.GetRequiredService<IAccessVersionStore>().SetAsync(demo, new Dictionary<string, long> { [subject] = 5 }, Ct);

        var stale = await host.Client("demo.localhost", host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)], subject: subject, accessVersion: 4))
            .GetAsync(TestPlatformHost.Path("/probe/user"), Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await Code(stale)).ShouldBe("platform.access-changed");
        stale.Headers.WwwAuthenticate.ToString().ShouldContain("invalid_token");

        (await host.Client("demo.localhost", host.Tokens.Create(demo, roles: [PurchaseRequest(Role.User)], subject: subject, accessVersion: 5))
            .GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rate_limits_come_from_the_plan_and_return_429_with_Retry_After()
    {
        await using var host = await TestPlatformHost.StartAsync(databases, limit: new PlanLimit { Plan = Plans.Standard, PerTenantPerMinute = 100, PerPrincipalPerMinute = 3 });
        var client = host.Client("demo.localhost", host.Tokens.Create(host.Tenants["demo"].Id, roles: [PurchaseRequest(Role.User)]));

        for (var i = 0; i < 3; i++)
            (await client.GetAsync(TestPlatformHost.Path("/probe/user"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var limited = await client.GetAsync(TestPlatformHost.Path("/probe/user"), Ct);
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task Removing_a_module_from_a_tenant_takes_effect_without_waiting_for_tokens()
    {
        await using var host = await TestPlatformHost.StartAsync(databases);
        var tenant = host.Tenants["demo-plus"];
        var client = host.Client("demo-plus.localhost", host.Tokens.Create(tenant.Id, roles: [RoleExtensions.ToModuleRole(Modules.CostCenterBudget, Role.User)]));
        (await client.GetAsync(TestPlatformHost.Path("/probe/budget"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<Core.Catalog.CatalogDbContext>();
            var budget = Modules.All.Single(m => m.Key == Modules.CostCenterBudget).Id;
            catalog.TenantModules.Remove(catalog.TenantModules.Single(t => t.TenantId == tenant.Id && t.ModuleId == budget));
            await catalog.SaveChangesAsync(Ct);
        }
        await host.Services.GetRequiredService<Core.Tenancy.ITenantChangeNotifier>().PublishAsync(tenant.Id, Ct);

        (await client.GetAsync(TestPlatformHost.Path("/probe/budget"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();
}
