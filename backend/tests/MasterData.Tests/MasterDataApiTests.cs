using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Farm360.Core.Platform;
using Farm360.Core.Security;
using Role = Farm360.Core.Platform.Role;

namespace Farm360.MasterData.Tests;

/// <summary>The module hosted exactly as in production, on every database provider in platform.yaml, with real migrations and tenants.</summary>
[Trait("service", "master-data")]
public sealed class MasterDataApiTests(Databases databases)
{
    private const string Api = "/api/master-data/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, Role)[] AsUser = [(Modules.MasterData, Role.User)];
    private static readonly (string, Role)[] AsManager = [(Modules.MasterData, Role.Manager)];

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement;
    }

    private static HttpRequestMessage WithIfMatch(HttpMethod method, string uri, string? eTag, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return request;
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Farms_are_created_listed_updated_and_deleted_with_the_roles_and_versions_the_rules_require(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<MasterDataModule>(databases, provider, null, "demo");
        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));

        var created = await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new
        {
            name = " Ladang Hijau ",
            code = "FRM-001",
            farmType = "greenhouse",
            globalGapNumber = "4049929999999",
            address = "Cameron Highlands, Pahang",
            areaHectares = 12.5m,
            latitude = 4.4721,
            longitude = 101.3801,
            orderSequence = 2,
            isExternal = false,
        }, Ct), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().ShouldBe("Ladang Hijau");
        created.GetProperty("farmType").GetString().ShouldBe("greenhouse");
        created.GetProperty("isActive").GetBoolean().ShouldBeTrue();
        created.GetProperty("isExternal").GetBoolean().ShouldBeFalse();

        // The location comes back as the same two numbers (stored as geography, SRID 4326).
        var read = await ReadAsync(await user.GetAsync($"{Api}/farms/{id}", Ct));
        read.GetProperty("latitude").GetDouble().ShouldBe(4.4721, 1e-9);
        read.GetProperty("longitude").GetDouble().ShouldBe(101.3801, 1e-9);
        read.GetProperty("areaHectares").GetDecimal().ShouldBe(12.5m);
        (await ReadAsync(await user.GetAsync($"{Api}/farms", Ct))).GetProperty("items").GetArrayLength().ShouldBe(1);

        // A change needs the version the client saw (428 without it). Leaving the location out clears it.
        (await user.PutAsJsonAsync($"{Api}/farms/{id}", new { name = "Ladang Biru" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        using var update = WithIfMatch(HttpMethod.Put, $"{Api}/farms/{id}", read.GetProperty("eTag").GetString(), new { name = "Ladang Biru", code = "FRM-001" });
        var updated = await ReadAsync(await user.SendAsync(update, Ct));
        updated.GetProperty("name").GetString().ShouldBe("Ladang Biru");
        updated.GetProperty("farmType").GetString().ShouldBe("field");
        updated.GetProperty("latitude").ValueKind.ShouldBe(JsonValueKind.Null);

        // Deleting needs a manager.
        var eTag = updated.GetProperty("eTag").GetString();
        using var delete = WithIfMatch(HttpMethod.Delete, $"{Api}/farms/{id}", eTag);
        (await user.SendAsync(delete, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var managerDelete = WithIfMatch(HttpMethod.Delete, $"{Api}/farms/{id}", eTag);
        (await manager.SendAsync(managerDelete, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await user.GetAsync($"{Api}/farms/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_code_belongs_to_one_farm_until_that_farm_is_deleted(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<MasterDataModule>(databases, provider, null, "demo");
        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));

        var first = await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new { name = "First", code = "FRM-001" }, Ct), HttpStatusCode.Created);
        var taken = await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new { name = "Second", code = " FRM-001 " }, Ct), HttpStatusCode.Conflict);
        taken.GetProperty("code").GetString().ShouldBe("master-data.code-in-use");

        // Farms without a code don't collide.
        await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new { name = "No code A" }, Ct), HttpStatusCode.Created);
        await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new { name = "No code B", code = "" }, Ct), HttpStatusCode.Created);

        using var delete = WithIfMatch(HttpMethod.Delete, $"{Api}/farms/{first.GetProperty("id").GetGuid()}", first.GetProperty("eTag").GetString());
        (await manager.SendAsync(delete, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", new { name = "Second", code = "FRM-001" }, Ct), HttpStatusCode.Created);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_tenant_never_sees_another_tenants_farms(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<MasterDataModule>(databases, provider, null, "demo", "other");
        var demo = host.Client("demo", host.Token("demo", roles: AsUser));
        var other = host.Client("other", host.Token("other", roles: AsUser));

        await ReadAsync(await demo.PostAsJsonAsync($"{Api}/farms", new { name = "Mine", code = "FRM-001" }, Ct), HttpStatusCode.Created);

        (await ReadAsync(await other.GetAsync($"{Api}/farms", Ct))).GetProperty("items").GetArrayLength().ShouldBe(0);
        // Codes are unique per tenant, not across tenants.
        await ReadAsync(await other.PostAsJsonAsync($"{Api}/farms", new { name = "Theirs", code = "FRM-001" }, Ct), HttpStatusCode.Created);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Callers_without_a_role_in_the_module_are_refused_and_bad_input_is_explained(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<MasterDataModule>(databases, provider, null, "demo");
        var stranger = host.Client("demo", host.Token("demo", type: PrincipalType.User));
        (await stranger.GetAsync($"{Api}/farms", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        async Task<string[]> FieldErrorsAsync(object body)
        {
            var response = await ReadAsync(await user.PostAsJsonAsync($"{Api}/farms", body, Ct), HttpStatusCode.BadRequest);
            return [.. response.GetProperty("errors").EnumerateObject().Select(e => e.Name)];
        }

        (await FieldErrorsAsync(new { name = "" })).ShouldContain("name");
        (await FieldErrorsAsync(new { name = "Farm", latitude = 91, longitude = 101 })).ShouldBe(["latitude"]);
        (await FieldErrorsAsync(new { name = "Farm", latitude = 3, longitude = 181 })).ShouldBe(["longitude"]);
        (await FieldErrorsAsync(new { name = "Farm", longitude = 101 })).ShouldBe(["latitude"]);
        (await FieldErrorsAsync(new { name = "Farm", areaHectares = -1 })).ShouldBe(["areaHectares"]);
        (await FieldErrorsAsync(new { name = "Farm", orderSequence = -1 })).ShouldBe(["orderSequence"]);
    }
}
