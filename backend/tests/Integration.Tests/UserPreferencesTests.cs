using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using static Saaskit.Integration.Tests.OperatorApi;

namespace Saaskit.Integration.Tests;

/// <summary>The language a person chooses: saved per user, only for supported locales, and never by a support session.</summary>
[Trait("service", "identity")]
public sealed class UserPreferencesTests(Databases databases)
{
    private const string Url = "/api/identity/v1/me/preferences";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Invites a user (so a user row exists) and returns a client calling as them, with no role anywhere.</summary>
    private static async Task<(HttpClient Client, Guid UserId)> UserAsync(IdentityTestHost host, string email, string? actor = null)
    {
        var admin = host.ApiClient("demo", (Modules.AccessManagement, Role.Admin));
        var invited = await Read(await admin.PostAsJsonAsync("/api/identity/v1/users/invitations", new { email, displayName = email, roles = Array.Empty<object>() }, Ct), HttpStatusCode.Created);
        var userId = invited.GetProperty("principalId").GetGuid();
        return (Caller(host, userId, actor), userId);
    }

    private static HttpClient Caller(IdentityTestHost host, Guid userId, string? actor = null, PrincipalType type = PrincipalType.User)
    {
        var client = host.Client("demo.localhost");
        client.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(
            host.Callers.Create(host.Tenants["demo"].Id, type, roles: [], audience: PlatformServices.Identity.Audience, subject: userId.ToString(), actor: actor));
        return client;
    }

    [Fact]
    public async Task A_language_is_saved_per_user_and_only_supported_locales_are_accepted()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo", "demo-complete");
        var (aisha, _) = await UserAsync(host, "aisha@example.com");
        var (budi, _) = await UserAsync(host, "budi@example.com");

        // No row yet: no preference, so the tenant's language applies.
        (await Read(await aisha.GetAsync(Url, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        var other = PlatformInfo.DefaultLocales.First(l => l != PlatformInfo.DefaultLocales[0]);
        (await Read(await aisha.PutAsJsonAsync(Url, new { locale = other }, Ct))).GetProperty("locale").GetString().ShouldBe(other);
        (await Read(await aisha.GetAsync(Url, Ct))).GetProperty("locale").GetString().ShouldBe(other);
        // Saved per person: another user of the same tenant is not changed.
        (await Read(await budi.GetAsync(Url, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        // Unsupported locales are refused and change nothing.
        var refused = await Read(await aisha.PutAsJsonAsync(Url, new { locale = "xx" }, Ct), HttpStatusCode.BadRequest);
        refused.GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldBe(["locale"]);
        (await Read(await aisha.GetAsync(Url, Ct))).GetProperty("locale").GetString().ShouldBe(other);

        // Changing it again updates the one row; empty goes back to the tenant's language.
        var first = PlatformInfo.DefaultLocales[0];
        (await Read(await aisha.PutAsJsonAsync(Url, new { locale = first }, Ct))).GetProperty("locale").GetString().ShouldBe(first);
        (await Read(await aisha.PutAsJsonAsync(Url, new { locale = (string?)null }, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        // Each tenant has its own people: a user of "demo" is no user of "demo-complete".
        var elsewhere = host.Client("demo-complete.localhost");
        elsewhere.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(host.Callers.Create(host.Tenants["demo-complete"].Id, PrincipalType.User, roles: [], audience: PlatformServices.Identity.Audience, subject: Guid.NewGuid().ToString()));
        (await elsewhere.PutAsJsonAsync(Url, new { locale = other }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Read(await elsewhere.GetAsync(Url, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_support_session_cannot_change_a_preference_and_machines_have_none()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var (_, userId) = await UserAsync(host, "customer@example.com");
        var other = PlatformInfo.DefaultLocales.First(l => l != PlatformInfo.DefaultLocales[0]);

        // The same user's token, but acting through an operator's support session (the act claim).
        var support = Caller(host, userId, actor: Guid.NewGuid().ToString());
        var refused = await Read(await support.PutAsJsonAsync(Url, new { locale = other }, Ct), HttpStatusCode.Forbidden);
        refused.GetProperty("code").GetString().ShouldBe("access-management.preferences-not-for-support");
        (await Read(await Caller(host, userId).GetAsync(Url, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        // Machines and services have no preferences.
        var machine = Caller(host, Guid.NewGuid(), type: PrincipalType.Machine);
        (await machine.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await machine.PutAsJsonAsync(Url, new { locale = other }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        // And nobody anonymous.
        (await host.Client("demo.localhost").GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_operator_chooses_a_console_language_that_comes_back_with_their_profile()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var other = PlatformInfo.DefaultLocales.First(l => l != PlatformInfo.DefaultLocales[0]);

        var me = await Read(await op.GetAsync($"{Api}/me", Ct));
        me.GetProperty("displayName").GetString().ShouldBe("Ops One");
        me.GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        (await Read(await op.PutAsJsonAsync($"{Api}/me/preferences", new { locale = other }, Ct))).GetProperty("locale").GetString().ShouldBe(other);
        (await Read(await op.GetAsync($"{Api}/me", Ct))).GetProperty("locale").GetString().ShouldBe(other);
        (await op.PutAsJsonAsync($"{Api}/me/preferences", new { locale = "xx" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Read(await op.PutAsJsonAsync($"{Api}/me/preferences", new { locale = (string?)null }, Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);

        // Another operator is not changed.
        var second = await SignInAsync(host, "ops2@example.com", "Ops Two");
        await Read(await op.PutAsJsonAsync($"{Api}/me/preferences", new { locale = other }, Ct));
        (await Read(await second.GetAsync($"{Api}/me", Ct))).GetProperty("locale").ValueKind.ShouldBe(JsonValueKind.Null);
    }
}
