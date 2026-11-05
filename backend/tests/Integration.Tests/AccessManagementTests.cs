using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Saaskit.Identity.Domain;
using Saaskit.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Saaskit.Integration.Tests;

[Trait("service", "identity")]
public sealed class AccessManagementTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Api = "/api/identity/v1";

    private static (string Module, Role Role)[] Admin => [(Modules.AccessManagement, Role.Admin)];

    private static async Task<T> Read<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } }, Ct))!;

    private static async Task<Guid> NewUserAsync(IdentityTestHost host, string name, string slug = "demo")
    {
        var admin = host.ApiClient(slug, Admin);
        var response = await admin.PostAsJsonAsync($"{Api}/users/invitations", new { email = $"{name}@example.com", displayName = name, roles = Array.Empty<object>() }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("principalId").GetGuid();
    }

    /// <summary>The Ali example (section 4.5), on a tenant where every module is entitled: the highest role always wins.</summary>
    [Fact]
    public async Task Direct_and_group_roles_combine_and_the_highest_wins()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo-plus");
        var admin = host.ApiClient("demo-plus", Admin);
        var ali = await NewUserAsync(host, "ali", "demo-plus");

        (await admin.PutAsJsonAsync($"{Api}/principals/{ali}/assignments/{Modules.TenantSettings}", new { role = "admin" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"{Api}/principals/{ali}/assignments/{Fixture.Optional}", new { role = "user" }, Ct)).EnsureSuccessStatusCode();

        var finance = await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "Finance" }, Ct));
        var group = finance.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"{Api}/groups/{group}/assignments/{Modules.TenantSettings}", new { role = "user" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"{Api}/groups/{group}/assignments/{Fixture.Optional}", new { role = "manager" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"{Api}/groups/{group}/assignments/{Modules.AccessManagement}", new { role = "user" }, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsync($"{Api}/groups/{group}/members/{ali}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var access = await Read<JsonElement>(await admin.GetAsync($"{Api}/principals/{ali}/access", Ct));
        access.GetProperty("roleClaims").EnumerateArray().Select(e => e.GetString()).ShouldBe(
            ["access-management:user", "billing:manager", "tenant-settings:admin"]);
    }

    [Fact]
    public async Task Entitlement_overrides_assignment_in_the_effective_access()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo", "demo-plus");
        var ali = await host.InTenantAsync("demo", async sp =>
        {
            var access = sp.GetRequiredService<Saaskit.Identity.Application.IAccessManagement>();
            var db = sp.GetRequiredService<IdentityDbContext>();
            var principal = Principal.NewUser("ali");
            db.Principals.Add(principal);
            await db.SaveChangesAsync(Ct);
            await access.SetPrincipalRoleAsync(principal.Id, Modules.TenantSettings, Role.Admin, Ct);
            await access.SetPrincipalRoleAsync(principal.Id, Fixture.Optional, Role.Manager, Ct);
            return await access.GetEffectiveAccessAsync(principal.Id, Ct);
        });
        // "demo" is not entitled to the optional module, so only tenant-settings remains.
        ali.RoleClaims.ShouldBe(["tenant-settings:admin"]);

        var plus = await host.InTenantAsync("demo-plus", async sp =>
        {
            var access = sp.GetRequiredService<Saaskit.Identity.Application.IAccessManagement>();
            var db = sp.GetRequiredService<IdentityDbContext>();
            var principal = Principal.NewUser("ali");
            db.Principals.Add(principal);
            await db.SaveChangesAsync(Ct);
            await access.SetPrincipalRoleAsync(principal.Id, Fixture.Optional, Role.Manager, Ct);
            return await access.GetEffectiveAccessAsync(principal.Id, Ct);
        });
        plus.RoleClaims.ShouldBe(["billing:manager"]);
    }

    [Fact]
    public async Task A_second_role_in_the_same_module_is_rejected_by_the_database()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var principal = Principal.NewUser("p");
            var group = new Group("g");
            db.Principals.Add(principal);
            db.Groups.Add(group);
            await db.SaveChangesAsync(Ct);
            var module = Modules.All.Single(m => m.Key == Modules.TenantSettings).Id;

            db.AccessAssignments.Add(AccessAssignment.ForPrincipal(principal.Id, module, Role.User));
            await db.SaveChangesAsync(Ct);
            db.AccessAssignments.Add(AccessAssignment.ForPrincipal(principal.Id, module, Role.Manager));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            db.ChangeTracker.Clear();

            db.AccessAssignments.Add(AccessAssignment.ForGroup(group.Id, module, Role.User));
            await db.SaveChangesAsync(Ct);
            db.AccessAssignments.Add(AccessAssignment.ForGroup(group.Id, module, Role.Admin));
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            db.ChangeTracker.Clear();

            // The same principal and group each may hold their own role in one module (the filter ignores NULLs).
            db.AccessAssignments.Add(AccessAssignment.ForPrincipal((await db.Principals.FirstAsync(Ct)).Id, Modules.All.Single(m => m.Key == Fixture.Optional).Id, Role.User));
            await db.SaveChangesAsync(Ct);
        });
    }

    [Fact]
    public async Task Changing_access_raises_access_version_for_the_principal_and_for_every_group_member()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var members = new[] { await NewUserAsync(host, "m1"), await NewUserAsync(host, "m2") };
        var outsider = await NewUserAsync(host, "outsider");
        var group = (await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "Team" }, Ct))).GetProperty("id").GetGuid();
        foreach (var member in members)
            (await admin.PutAsync($"{Api}/groups/{group}/members/{member}", null, Ct)).EnsureSuccessStatusCode();

        var before = await VersionsAsync(host, [.. members, outsider]);
        (await admin.PutAsJsonAsync($"{Api}/groups/{group}/assignments/{Modules.TenantSettings}", new { role = "manager" }, Ct)).EnsureSuccessStatusCode();
        var after = await VersionsAsync(host, [.. members, outsider]);

        foreach (var member in members)
            after[member].ShouldBeGreaterThan(before[member]);
        after[outsider].ShouldBe(before[outsider]);

        // The new version is also in Redis, so services reject older tokens: an old token no longer works.
        var stale = host.Callers.Create(host.Tenants["demo"].Id, PrincipalType.User, ["access-management:admin"], audience: PlatformServices.Identity.Audience, subject: members[0].ToString(), accessVersion: before[members[0]]);
        var client = host.Client("demo.localhost");
        client.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(stale);
        (await client.GetAsync($"{Api}/groups", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Readers_and_writers_need_the_right_role_and_services_can_join_groups()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var user = host.ApiClient("demo", (Modules.AccessManagement, Role.User));
        var manager = host.ApiClient("demo", (Modules.AccessManagement, Role.Manager));
        var admin = host.ApiClient("demo", Admin);

        (await user.GetAsync($"{Api}/groups", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"{Api}/groups", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await manager.PostAsJsonAsync($"{Api}/groups", new { displayName = "x" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var group = (await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "Ops" }, Ct))).GetProperty("id").GetGuid();
        (await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "ops" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);   // names are unique ignoring case

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonSerializer.Serialize(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(key) { KeyId = "k1" }) is { } k ? WithoutPrivate(k) : null);
        var service = await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/services", new { displayName = "erp", publicKeyJwk = jwk }, Ct));
        var serviceId = service.GetProperty("id").GetGuid();
        var before = (await VersionsAsync(host, [serviceId]))[serviceId];
        (await admin.PutAsync($"{Api}/groups/{group}/members/{serviceId}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The application is a member like any other: it is listed, and its access version rises so older tokens stop working.
        var members = await Read<JsonElement>(await admin.GetAsync($"{Api}/groups/{group}/members", Ct));
        members.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).ShouldBe([serviceId]);
        (await VersionsAsync(host, [serviceId]))[serviceId].ShouldBeGreaterThan(before);
    }

    [Fact]
    public async Task A_machine_enrolls_once_with_its_own_public_key_and_then_signs_in()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var created = await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/machines", new { displayName = "Packing tablet" }, Ct));
        var machineId = created.GetProperty("principalId").GetGuid();
        var code = created.GetProperty("code").GetString()!;
        (await admin.PutAsJsonAsync($"{Api}/principals/{machineId}/assignments/{Modules.TenantSettings}", new { role = "user" }, Ct)).EnsureSuccessStatusCode();

        // The device makes its own key pair and sends only the public part with the code.
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var securityKey = new ECDsaSecurityKey(deviceKey) { KeyId = "device-key-1" };
        var jwk = WithoutPrivate(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(securityKey));
        var anonymous = host.Client("demo.localhost");
        var enrolled = await anonymous.PostAsJsonAsync($"{Api}/machines/enroll", new { code, publicKeyJwk = JsonSerializer.Serialize(jwk) }, Ct);
        enrolled.StatusCode.ShouldBe(HttpStatusCode.OK, await enrolled.Content.ReadAsStringAsync(Ct));
        var clientId = (await enrolled.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("clientId").GetString()!;

        // The code is single use.
        (await anonymous.PostAsJsonAsync($"{Api}/machines/enroll", new { code, publicKeyJwk = JsonSerializer.Serialize(jwk) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var token = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["resource"] = PlatformServices.Tenancy.Audience,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = IdentityTestHost.ClientAssertion(clientId, deviceKey, "device-key-1"),
        }), Ct);
        token.StatusCode.ShouldBe(HttpStatusCode.OK, await token.Content.ReadAsStringAsync(Ct));
        var access = new JsonWebTokenHandler().ReadJsonWebToken((await token.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString());
        access.GetClaim("principal_type").Value.ShouldBe("machine");
        access.GetClaim("roles").Value.ShouldBe("tenant-settings:user");

        // Disabling the machine stops it signing in immediately.
        var fresh = await Read<JsonElement>(await admin.GetAsync($"{Api}/principals/{machineId}", Ct));
        var disable = new HttpRequestMessage(HttpMethod.Put, $"{Api}/principals/{machineId}/active") { Content = JsonContent.Create(new { active = false }) };
        disable.Headers.TryAddWithoutValidation("If-Match", fresh.GetProperty("eTag").GetString());
        (await admin.SendAsync(disable, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var denied = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["resource"] = PlatformServices.Tenancy.Audience,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = IdentityTestHost.ClientAssertion(clientId, deviceKey, "device-key-1"),
        }), Ct);
        denied.StatusCode.ShouldNotBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotation_keeps_two_keys_valid_at_once()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var oldKey = await host.RegisterMachineAsync("demo", "rotating", roles: [(Modules.TenantSettings, Role.User)]);
        var machineId = await host.InTenantAsync("demo", async sp => (await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.ClientId == "rotating", Ct)).Id);
        var oldKid = await OnlyKidAsync(host, "rotating");

        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = WithoutPrivate(JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(newKey) { KeyId = "new-key" }));
        var admin = host.ApiClient("demo", Admin);
        (await admin.PostAsJsonAsync($"{Api}/principals/{machineId}/keys", new { publicKeyJwk = JsonSerializer.Serialize(jwk) }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        async Task<HttpStatusCode> SignInAsync(ECDsa key, string kid) =>
            (await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials", ["client_id"] = "rotating", ["resource"] = PlatformServices.Tenancy.Audience,
                ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
                ["client_assertion"] = IdentityTestHost.ClientAssertion("rotating", key, kid),
            }), Ct)).StatusCode;

        (await SignInAsync(oldKey, oldKid)).ShouldBe(HttpStatusCode.OK);
        (await SignInAsync(newKey, "new-key")).ShouldBe(HttpStatusCode.OK);

        (await admin.DeleteAsync($"{Api}/principals/{machineId}/keys/{oldKid}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SignInAsync(oldKey, oldKid)).ShouldNotBe(HttpStatusCode.OK);
        (await SignInAsync(newKey, "new-key")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Every_access_change_is_written_to_the_append_only_audit_log()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var user = await NewUserAsync(host, "audited");
        (await admin.PutAsJsonAsync($"{Api}/principals/{user}/assignments/{Modules.TenantSettings}", new { role = "user" }, Ct)).EnsureSuccessStatusCode();
        var group = (await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "Auditors" }, Ct))).GetProperty("id").GetGuid();
        (await admin.PutAsync($"{Api}/groups/{group}/members/{user}", null, Ct)).EnsureSuccessStatusCode();

        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var kinds = await db.AuditLog.Select(a => a.Kind).ToListAsync(Ct);
            kinds.ShouldContain(AuditKinds.InvitationCreated);
            kinds.ShouldContain(AuditKinds.AssignmentChanged);
            kinds.ShouldContain(AuditKinds.GroupChanged);
            kinds.ShouldContain(AuditKinds.MembershipChanged);

            // Append only: entries can't be changed or deleted.
            var entry = await db.AuditLog.FirstAsync(Ct);
            db.AuditLog.Remove(entry);
            await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(Ct));
        });
    }

    private static JsonWebKey WithoutPrivate(JsonWebKey key)
    {
        key.D = null;
        return key;
    }

    private static async Task<string> OnlyKidAsync(IdentityTestHost host, string clientId, string slug = "demo") =>
        await host.InTenantAsync(slug, async sp =>
        {
            var app = await sp.GetRequiredService<IdentityCatalogDbContext>().OidcApplications.SingleAsync(a => a.ClientId == clientId, Ct);
            return new JsonWebKeySet(app.JsonWebKeySet).Keys.Single().Kid;
        });

    private static async Task<Dictionary<Guid, long>> VersionsAsync(IdentityTestHost host, Guid[] ids) =>
        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            return await db.Principals.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.AccessVersion, Ct);
        });

    /// <summary>A principal with the highest role in every module, plus data scopes, still fits a 4 KB token.</summary>
    [Fact]
    public async Task A_token_for_a_principal_with_every_module_stays_under_4_KB()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo-plus");
        var key = await host.RegisterMachineAsync("demo-plus", "everything", roles: [.. Modules.All.Select(m => (m.Key, Role.Admin))]);
        await host.InTenantAsync("demo-plus", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var machine = await db.Principals.SingleAsync(p => p.ClientId == "everything", Ct);
            for (var site = 0; site < 40; site++)
                db.DataScopeAssignments.Add(DataScopeAssignment.ForPrincipal(machine.Id, "site", $"site-{site:D4}"));
            await db.SaveChangesAsync(Ct);
        });
        var kid = await OnlyKidAsync(host, "everything", "demo-plus");

        var response = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "everything", ["resource"] = PlatformServices.Tenancy.Audience,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = IdentityTestHost.ClientAssertion("everything", key, kid),
        }), Ct);
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString()!;

        System.Text.Encoding.UTF8.GetByteCount(token).ShouldBeLessThan(4096);
        new JsonWebTokenHandler().ReadJsonWebToken(token).Claims.Count(c => c.Type == "roles").ShouldBe(Modules.All.Count);
    }

    // ---- The roles in a real access token: direct and group roles combined, the highest per module, limited to the plan ----------

    private static async Task<Guid> NewGroupAsync(HttpClient admin, string name, params (string Module, string Role)[] roles)
    {
        var group = (await Read<JsonElement>(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = name }, Ct))).GetProperty("id").GetGuid();
        foreach (var (module, role) in roles)
            (await admin.PutAsJsonAsync($"{Api}/groups/{group}/assignments/{module}", new { role }, Ct)).EnsureSuccessStatusCode();
        return group;
    }

    /// <summary>The <c>roles</c> claims AccessPrincipalFactory puts in the token of a user (the same factory the token endpoint uses for every principal type).</summary>
    private static async Task<string[]> RolesInTokenAsync(IdentityTestHost host, string slug, Guid principalId) =>
        await host.InTenantAsync(slug, async sp =>
        {
            var principal = await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.Id == principalId, Ct);
            var claims = await sp.GetRequiredService<AccessPrincipalFactory>().CreateAsync(principal, "Bearer", [], Ct);
            return claims.FindAll(PlatformClaims.Roles).Select(c => c.Value).Order(StringComparer.Ordinal).ToArray();
        });

    /// <summary>Signs a device or an application in at the token endpoint, as it would, and returns its access token.</summary>
    private static async Task<Microsoft.IdentityModel.JsonWebTokens.JsonWebToken> TokenForAsync(IdentityTestHost host, string slug, string clientId, ECDsa key)
    {
        var kid = await OnlyKidAsync(host, clientId, slug);
        var response = await host.Client().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = clientId, ["resource"] = PlatformServices.Tenancy.Audience,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = IdentityTestHost.ClientAssertion(clientId, key, kid),
        }), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return new JsonWebTokenHandler().ReadJsonWebToken((await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("access_token").GetString());
    }

    private static string[] RoleClaims(Microsoft.IdentityModel.JsonWebTokens.JsonWebToken token) =>
        [.. token.Claims.Where(c => c.Type == PlatformClaims.Roles).Select(c => c.Value).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task A_user_with_admin_on_base_directly_and_user_through_a_group_has_base_admin_in_the_token()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var ali = await NewUserAsync(host, "ali");
        (await admin.PutAsJsonAsync($"{Api}/principals/{ali}/assignments/{Modules.Base}", new { role = "admin" }, Ct)).EnsureSuccessStatusCode();
        var group = await NewGroupAsync(admin, "Base users", (Modules.Base, "user"));
        (await admin.PutAsync($"{Api}/groups/{group}/members/{ali}", null, Ct)).EnsureSuccessStatusCode();

        (await RolesInTokenAsync(host, "demo", ali)).ShouldBe([$"{Modules.Base}:admin"]);
    }

    [Fact]
    public async Task A_user_with_a_role_only_through_a_group_has_it_in_the_token_and_loses_it_when_removed()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var bea = await NewUserAsync(host, "bea");
        var group = await NewGroupAsync(admin, "Base managers", (Modules.Base, "manager"));
        (await admin.PutAsync($"{Api}/groups/{group}/members/{bea}", null, Ct)).EnsureSuccessStatusCode();
        (await RolesInTokenAsync(host, "demo", bea)).ShouldBe([$"{Modules.Base}:manager"]);

        (await admin.DeleteAsync($"{Api}/groups/{group}/members/{bea}", Ct)).EnsureSuccessStatusCode();
        (await RolesInTokenAsync(host, "demo", bea)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_device_combines_its_own_role_and_its_groups_in_the_token_the_token_endpoint_issues()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var key = await host.RegisterMachineAsync("demo", "tablet-1", PrincipalType.Machine, [(Modules.Base, Role.User)]);
        var machine = await host.InTenantAsync("demo", async sp => (await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.ClientId == "tablet-1", Ct)).Id);
        var group = await NewGroupAsync(admin, "Packing", (Modules.Base, "manager"), (Modules.TenantSettings, "user"));
        (await admin.PutAsync($"{Api}/groups/{group}/members/{machine}", null, Ct)).EnsureSuccessStatusCode();

        var token = await TokenForAsync(host, "demo", "tablet-1", key);

        token.GetClaim(PlatformClaims.PrincipalType).Value.ShouldBe("machine");
        RoleClaims(token).ShouldBe([$"{Modules.Base}:manager", $"{Modules.TenantSettings}:user"]);
    }

    [Fact]
    public async Task An_application_in_a_group_gets_the_groups_roles_and_its_own_highest_role_in_the_token()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", Admin);
        var groupOnly = await host.RegisterMachineAsync("demo", "erp", PrincipalType.Service);
        var both = await host.RegisterMachineAsync("demo", "partner", PrincipalType.Service, [(Modules.Base, Role.Admin)]);
        var ids = await host.InTenantAsync("demo", async sp =>
            await sp.GetRequiredService<IdentityDbContext>().Principals.Where(p => p.ClientId == "erp" || p.ClientId == "partner").ToDictionaryAsync(p => p.ClientId!, p => p.Id, Ct));
        var group = await NewGroupAsync(admin, "Integrations", (Modules.Base, "user"));
        foreach (var id in ids.Values)
            (await admin.PutAsync($"{Api}/groups/{group}/members/{id}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var erp = await TokenForAsync(host, "demo", "erp", groupOnly);
        erp.GetClaim(PlatformClaims.PrincipalType).Value.ShouldBe("service");
        RoleClaims(erp).ShouldBe([$"{Modules.Base}:user"]);
        RoleClaims(await TokenForAsync(host, "demo", "partner", both)).ShouldBe([$"{Modules.Base}:admin"]);
    }

    [Fact]
    public async Task A_module_outside_the_tenants_plan_is_left_out_of_the_token_however_it_was_granted()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");   // "demo" has every module except the fixture's optional one
        var admin = host.ApiClient("demo", Admin);
        var key = await host.RegisterMachineAsync("demo", "gate", PrincipalType.Service, [(Modules.Base, Role.User)]);
        var (user, service) = (await NewUserAsync(host, "cy"), await host.InTenantAsync("demo", async sp => (await sp.GetRequiredService<IdentityDbContext>().Principals.SingleAsync(p => p.ClientId == "gate", Ct)).Id));
        var optional = Modules.All.Single(m => m.Key == Fixture.Optional).Id;
        var group = await NewGroupAsync(admin, "Everything", (Modules.Base, "user"));
        await host.InTenantAsync("demo", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            db.AccessAssignments.Add(AccessAssignment.ForPrincipal(user, optional, Role.Admin));   // granted around the API, which would refuse it
            db.AccessAssignments.Add(AccessAssignment.ForGroup(group, optional, Role.Admin));
            await db.SaveChangesAsync(Ct);
        });
        (await admin.PutAsync($"{Api}/groups/{group}/members/{user}", null, Ct)).EnsureSuccessStatusCode();
        (await admin.PutAsync($"{Api}/groups/{group}/members/{service}", null, Ct)).EnsureSuccessStatusCode();

        (await RolesInTokenAsync(host, "demo", user)).ShouldBe([$"{Modules.Base}:user"]);
        RoleClaims(await TokenForAsync(host, "demo", "gate", key)).ShouldBe([$"{Modules.Base}:user"]);
    }
}
