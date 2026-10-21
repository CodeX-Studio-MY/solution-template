using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;

namespace Saaskit.Integration.Tests;

/// <summary>What the Settings screens need beyond roles: emails and status in lists, rename, group members, resending an invitation.</summary>
[Trait("service", "identity")]
public sealed class AccessManagementCrudTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Api = "/api/identity/v1";

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, body);
        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Users_are_listed_with_email_and_status_renamed_with_If_Match_and_listed_as_group_members()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", (Modules.AccessManagement, Role.Admin));
        var invited = await Read(await admin.PostAsJsonAsync($"{Api}/users/invitations", new { email = "siti@example.com", displayName = "Siti", roles = Array.Empty<object>() }, Ct), HttpStatusCode.Created);
        var id = invited.GetProperty("principalId").GetGuid();

        var listed = (await Read(await admin.GetAsync($"{Api}/principals?type=user", Ct))).EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == id);
        listed.GetProperty("email").GetString().ShouldBe("siti@example.com");
        listed.GetProperty("status").GetString().ShouldBe("invited");

        var rename = new HttpRequestMessage(HttpMethod.Put, $"{Api}/principals/{id}") { Content = JsonContent.Create(new { displayName = "Siti Aminah" }) };
        rename.Headers.TryAddWithoutValidation("If-Match", listed.GetProperty("eTag").GetString());
        (await Read(await admin.SendAsync(rename, Ct))).GetProperty("displayName").GetString().ShouldBe("Siti Aminah");

        var stale = new HttpRequestMessage(HttpMethod.Put, $"{Api}/principals/{id}") { Content = JsonContent.Create(new { displayName = "Again" }) };
        stale.Headers.TryAddWithoutValidation("If-Match", listed.GetProperty("eTag").GetString());
        (await admin.SendAsync(stale, Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await admin.PutAsJsonAsync($"{Api}/principals/{id}", new { displayName = " " }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var group = (await Read(await admin.PostAsJsonAsync($"{Api}/groups", new { displayName = "Field" }, Ct), HttpStatusCode.Created)).GetProperty("id").GetGuid();
        (await admin.PutAsync($"{Api}/groups/{group}/members/{id}", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var manager = host.ApiClient("demo", (Modules.AccessManagement, Role.Manager));
        var members = (await Read(await manager.GetAsync($"{Api}/groups/{group}/members", Ct))).EnumerateArray().ToList();
        members.Single().GetProperty("displayName").GetString().ShouldBe("Siti Aminah");
    }

    [Fact]
    public async Task Resending_an_invitation_sends_a_new_link_and_only_while_it_is_pending()
    {
        await using var host = await IdentityTestHost.StartAsync(databases, "demo");
        var admin = host.ApiClient("demo", (Modules.AccessManagement, Role.Admin));
        var id = (await Read(await admin.PostAsJsonAsync($"{Api}/users/invitations", new { email = "lee@example.com", displayName = "Lee", roles = Array.Empty<object>() }, Ct), HttpStatusCode.Created))
            .GetProperty("principalId").GetGuid();
        var first = host.Messages.Links.Last(l => l.Email == "lee@example.com").Url;

        (await admin.PostAsync($"{Api}/users/{id}/invitation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = host.Messages.Links.Last(l => l.Email == "lee@example.com").Url;
        second.ShouldNotBe(first);

        var manager = host.ApiClient("demo", (Modules.AccessManagement, Role.Manager));
        (await manager.PostAsync($"{Api}/users/{id}/invitation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"{Api}/users/{Guid.NewGuid()}/invitation", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
