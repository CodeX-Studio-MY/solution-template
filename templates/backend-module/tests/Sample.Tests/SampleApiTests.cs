using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Core.Security;
using Role = Saaskit.Core.Platform.Role;

namespace Saaskit.Sample.Tests;

/// <summary>The module hosted exactly as in production, on every database provider in platform.yaml, with real migrations and tenants.</summary>
[Trait("service", "sample-key")]
public sealed class SampleApiTests(Databases databases)
{
    private const string Api = "/api/sample-key/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, Role)[] AsUser = [(Modules.Sample, Role.User)];
    private static readonly (string, Role)[] AsManager = [(Modules.Sample, Role.Manager)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Items_are_created_listed_renamed_and_deleted_with_the_roles_and_versions_the_rules_require(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<SampleModule>(databases, provider, null, "demo");
        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));

        var created = await ReadAsync(await user.PostAsJsonAsync($"{Api}/items", new { name = "Widget" }, Ct), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        (await ReadAsync(await user.GetAsync($"{Api}/items", Ct))).GetProperty("items").GetArrayLength().ShouldBe(1);

        // A change needs the version the client saw (428 without it, 412 when stale).
        (await user.PutAsJsonAsync($"{Api}/items/{id}", new { name = "Gadget" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        using var rename = new HttpRequestMessage(HttpMethod.Put, $"{Api}/items/{id}") { Content = JsonContent.Create(new { name = "Gadget" }) };
        rename.Headers.TryAddWithoutValidation("If-Match", created.GetProperty("eTag").GetString());
        (await ReadAsync(await user.SendAsync(rename, Ct))).GetProperty("name").GetString().ShouldBe("Gadget");

        // Deleting needs a manager.
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/items/{id}");
        delete.Headers.TryAddWithoutValidation("If-Match", (await ReadAsync(await user.GetAsync($"{Api}/items/{id}", Ct))).GetProperty("eTag").GetString());
        (await user.SendAsync(delete, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var managerDelete = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/items/{id}");
        managerDelete.Headers.TryAddWithoutValidation("If-Match", delete.Headers.GetValues("If-Match").Single());
        (await manager.SendAsync(managerDelete, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetAsync($"{Api}/items/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_tenant_never_sees_another_tenants_items(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<SampleModule>(databases, provider, null, "demo", "other");
        var demo = host.Client("demo", host.Token("demo", roles: AsUser));
        var other = host.Client("other", host.Token("other", roles: AsUser));

        await ReadAsync(await demo.PostAsJsonAsync($"{Api}/items", new { name = "Mine" }, Ct), HttpStatusCode.Created);

        (await ReadAsync(await other.GetAsync($"{Api}/items", Ct))).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Callers_without_a_role_in_the_module_are_refused_and_bad_input_is_explained(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<SampleModule>(databases, provider, null, "demo");
        var stranger = host.Client("demo", host.Token("demo", type: PrincipalType.User));
        (await stranger.GetAsync($"{Api}/items", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var invalid = await user.PostAsJsonAsync($"{Api}/items", new { name = "" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync(invalid, HttpStatusCode.BadRequest)).GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldContain("name");
    }
}
