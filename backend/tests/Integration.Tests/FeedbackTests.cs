using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Saaskit.Core.Platform;
using Saaskit.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Saaskit.Integration.Tests.OperatorApi;

namespace Saaskit.Integration.Tests;

/// <summary>Feedback: any user of a tenant sends it, that tenant's admins read it, and the operator reads every tenant's and triages it.</summary>
[Trait("service", "tenancy")]
public sealed class FeedbackTests(Databases databases)
{
    private const string Url = "/api/tenancy/v1/feedback";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpClient Caller(TenancyTestHost host, string slug, Guid tenantId, string[] roles, string? subject = null, string? actor = null)
    {
        var client = host.Client($"{slug}.localhost");
        client.DefaultRequestHeaders.Authorization = IdentityTestHost.Bearer(
            host.Identity.Callers.Create(tenantId, roles: roles, audience: PlatformServices.Tenancy.Audience, subject: subject, actor: actor));
        return client;
    }

    private static object Message(string text = "The export button does nothing", object? rating = null) =>
        new { kind = "bug", rating = rating ?? 4, message = text, pagePath = "/settings/support-access" };

    private static async Task<JsonElement> OperatorListAsync(HttpClient op, string query = "") =>
        await Read(await op.GetAsync($"{Api}/feedback{query}", Ct));

    [Fact]
    public async Task A_user_sends_feedback_their_admin_reads_it_and_other_tenants_cannot()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var tenantId = await OnboardAsync(op, "speaks", Fixture.BasePlan);
        var otherId = await OnboardAsync(op, "elsewhere", Fixture.BasePlan);

        // The first admin's name is on the snapshot; a user with no role anywhere may still send.
        // (The invited admin isn't active until they accept, and Identity only names active users.)
        var senderId = await host.Identity.InTenantAsync("speaks", async sp =>
        {
            var db = sp.GetRequiredService<IdentityDbContext>();
            var principal = await db.Principals.SingleAsync(Ct);
            principal.Enable();
            await db.SaveChangesAsync(Ct);
            return principal.Id;
        });
        var user = Caller(host, "speaks", tenantId, roles: [], subject: senderId.ToString());
        var sent = await user.PostAsJsonAsync(Url, Message(), Ct);
        var row = await Read(sent, HttpStatusCode.Created);
        row.GetProperty("status").GetString().ShouldBe("new");
        row.GetProperty("senderName").GetString().ShouldBe("A");
        row.GetProperty("kind").GetString().ShouldBe("bug");
        row.GetProperty("rating").GetInt32().ShouldBe(4);
        row.GetProperty("pagePath").GetString().ShouldBe("/settings/support-access");
        ETag(sent).ShouldNotBeEmpty();

        // A rating is optional.
        await Read(await user.PostAsJsonAsync(Url, new { kind = "idea", message = "Dark mode, please", pagePath = "" }, Ct), HttpStatusCode.Created);

        // The tenant's admin lists both, newest first.
        var admin = Caller(host, "speaks", tenantId, ["feedback:admin"]);
        var listed = await Read(await admin.GetAsync(Url, Ct));
        listed.GetProperty("totalCount").GetInt32().ShouldBe(2);
        listed.GetProperty("items")[0].GetProperty("message").GetString().ShouldBe("Dark mode, please");

        // A user without the role gets 403, and another tenant's admin sees none of it.
        (await user.GetAsync(Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var otherAdmin = Caller(host, "elsewhere", otherId, ["feedback:admin"]);
        (await Read(await otherAdmin.GetAsync(Url, Ct))).GetProperty("totalCount").GetInt32().ShouldBe(0);

        // A support session acts for the customer and can't send in their name.
        var support = Caller(host, "speaks", tenantId, [], subject: senderId.ToString(), actor: """{"sub":"operator-1"}""");
        (await support.PostAsJsonAsync(Url, Message(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Feedback_needs_a_message_and_a_rating_from_one_to_five()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var tenantId = await OnboardAsync(op, "strict", Fixture.BasePlan);
        var user = Caller(host, "strict", tenantId, roles: [], subject: Guid.NewGuid().ToString());

        (await user.PostAsJsonAsync(Url, Message(rating: 6), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostAsJsonAsync(Url, Message(rating: 0), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostAsJsonAsync(Url, Message("   "), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostAsJsonAsync(Url, Message(new string('x', 2001)), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await user.PostAsJsonAsync(Url, new { kind = "bug", message = "Hi", pagePath = "/café" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OperatorListAsync(op)).GetProperty("totalCount").GetInt32().ShouldBe(0);

        // The edges are accepted. An unknown sender falls back to the ID the token carries.
        await Read(await user.PostAsJsonAsync(Url, Message(new string('x', 2000), rating: 1), Ct), HttpStatusCode.Created);
        await Read(await user.PostAsJsonAsync(Url, Message(rating: 5), Ct), HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_operator_reads_every_tenants_feedback_and_changes_its_status_with_an_ETag()
    {
        await using var host = await TenancyTestHost.StartAsync(databases);
        var op = await SignInAsync(host);
        var firstId = await OnboardAsync(op, "first-co", Fixture.BasePlan);
        var secondId = await OnboardAsync(op, "second-co", Fixture.BasePlan);
        var first = Caller(host, "first-co", firstId, roles: [], subject: Guid.NewGuid().ToString());
        var second = Caller(host, "second-co", secondId, roles: [], subject: Guid.NewGuid().ToString());
        await Read(await first.PostAsJsonAsync(Url, Message("From the first"), Ct), HttpStatusCode.Created);
        await Read(await second.PostAsJsonAsync(Url, Message("From the second"), Ct), HttpStatusCode.Created);

        var all = await OperatorListAsync(op);
        all.GetProperty("totalCount").GetInt32().ShouldBe(2);
        all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tenantSlug").GetString()).Order().ShouldBe(["first-co", "second-co"]);
        var onlyFirst = await OperatorListAsync(op, $"?tenantId={firstId}");
        onlyFirst.GetProperty("items").EnumerateArray().Single().GetProperty("message").GetString().ShouldBe("From the first");

        var item = all.GetProperty("items").EnumerateArray().First(i => i.GetProperty("tenantSlug").GetString() == "first-co");
        var id = item.GetProperty("feedbackId").GetGuid();
        var eTag = item.GetProperty("eTag").GetString()!;
        var path = $"{Api}/feedback/{id}/status";

        // 428 without If-Match.
        (await op.PostAsJsonAsync(path, new { status = "reviewed" }, Ct)).StatusCode.ShouldBe((HttpStatusCode)428);

        var changed = await PostWithETagAsync(op, path, new { status = "reviewed" }, eTag);
        (await Read(changed)).GetProperty("status").GetString().ShouldBe("reviewed");
        ETag(changed).ShouldNotBe(eTag);

        // 412 when the ETag is stale.
        (await PostWithETagAsync(op, path, new { status = "done" }, eTag)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await Read(await PostWithETagAsync(op, path, new { status = "done" }, ETag(changed)))).GetProperty("status").GetString().ShouldBe("done");

        // Filters, and a missing item.
        (await OperatorListAsync(op, "?status=done")).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await OperatorListAsync(op, "?status=new")).GetProperty("items").GetArrayLength().ShouldBe(1);
        (await PostWithETagAsync(op, $"{Api}/feedback/{Guid.NewGuid()}/status", new { status = "done" }, eTag)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Each change is in the tenant's audit trail; a tenant's own users can't use the operator's endpoints.
        var activity = await Read(await op.GetAsync($"{Api}/tenants/{firstId}/activity", Ct));
        activity.GetProperty("items").EnumerateArray().Count(a => a.GetProperty("kind").GetString() == "feedback-status-changed").ShouldBe(2);
        var admin = Caller(host, "first-co", firstId, ["feedback:admin"]);
        (await admin.GetAsync($"{Api}/feedback", Ct)).StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string path, object body, string? eTag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (eTag is not null)
            request.Headers.TryAddWithoutValidation("If-Match", eTag);
        return client.SendAsync(request, Ct);
    }
}
