using System.Net.Http.Json;
using System.Text.Json;
using Acme.Contracts;
using Acme.Core.Platform;
using Acme.Notification;
using Acme.Notification.Application;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Acme.Integration.Tests;

/// <summary>Notification: events become history, a real-time push and an email, per person and per tenant.</summary>
[Collection(BrokerCollection.Name)]
public sealed class NotificationTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed class FakeContacts : IContactDirectory
    {
        public Dictionary<Guid, Contact> People { get; } = [];

        public Task<Contact?> FindAsync(Guid principalId, CancellationToken cancellationToken = default) => Task.FromResult(People.GetValueOrDefault(principalId));

        public Task<IReadOnlyList<Contact>> ApproversOfAsync(string moduleKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contact>>(People.Values.Where(p => p.DisplayName.StartsWith("Approver", StringComparison.Ordinal)).ToList());
    }

    private async Task<ModuleTestHost> StartAsync(string provider, FakeContacts contacts, params string[] tenants) =>
        await ModuleTestHost.StartAsync<NotificationModule>(databases, provider, builder =>
        {
            builder.Configuration["Smtp:Host"] = databases.Smtp.Host;
            builder.Configuration["Smtp:Port"] = databases.Smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.Configuration["Smtp:FromAddress"] = "no-reply@acme.test";
            builder.Services.RemoveAll<IContactDirectory>();
            builder.Services.AddSingleton<IContactDirectory>(contacts);
        }, tenants);

    private static PurchaseRequestApproved.V1 Approved(Guid requester, string number) =>
        new(Guid.NewGuid(), number, requester, Guid.NewGuid(), 1234.5m, "MYR", new DateOnly(2026, 10, 9), AutoApproved: false);

    private static async Task<JsonElement> WaitForListAsync(HttpClient client, int expected)
    {
        for (var i = 0; i < 100; i++)
        {
            var response = await client.GetAsync(new Uri("/api/notification/v1/notifications", UriKind.Relative), Ct);
            response.EnsureSuccessStatusCode();
            var page = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
            if (page.GetProperty("items").GetArrayLength() >= expected)
                return page;
            await Task.Delay(300, Ct);
        }
        throw new TimeoutException("The notification never arrived.");
    }

    private async Task<JsonElement> WaitForMailAsync(string recipient)
    {
        using var http = new HttpClient { BaseAddress = databases.MailpitApi };
        for (var i = 0; i < 100; i++)
        {
            var search = await http.GetFromJsonAsync<JsonElement>(new Uri($"/api/v1/search?query=to:{recipient}", UriKind.Relative), Ct);
            if (search.GetProperty("messages_count").GetInt32() > 0)
                return search.GetProperty("messages")[0];
            await Task.Delay(300, Ct);
        }
        throw new TimeoutException("The email never arrived.");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task An_approval_becomes_history_for_the_requester_and_an_email(string provider)
    {
        var requester = Guid.NewGuid();
        var address = $"req-{Guid.NewGuid():N}@acme.test";
        var contacts = new FakeContacts();
        contacts.People[requester] = new Contact(requester, "Rita Requester", address);
        await using var host = await StartAsync(provider, contacts, "demo");

        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(requester, "PR-2026-0001"), cancellationToken: Ct);

        using var client = host.Client("demo", host.Token("demo", requester));
        var page = await WaitForListAsync(client, 1);
        var item = page.GetProperty("items")[0];
        item.GetProperty("kind").GetString().ShouldBe("purchase-request.approved");
        item.GetProperty("title").GetString()!.ShouldContain("PR-2026-0001");
        item.GetProperty("isRead").GetBoolean().ShouldBeFalse();
        item.GetProperty("link").GetString()!.ShouldStartWith($"/{Modules.PurchaseRequest}/");

        var mail = await WaitForMailAsync(address);
        mail.GetProperty("Subject").GetString()!.ShouldContain("PR-2026-0001");

        var unread = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications/unread-count", UriKind.Relative), Json, Ct);
        unread.GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Reading_marks_one_or_all_and_people_see_only_their_own(string provider)
    {
        var rita = Guid.NewGuid();
        var sam = Guid.NewGuid();
        var contacts = new FakeContacts();
        contacts.People[rita] = new Contact(rita, "Rita", null);   // no address: history only, no email
        contacts.People[sam] = new Contact(sam, "Sam", null);
        await using var host = await StartAsync(provider, contacts, "demo");

        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(rita, "PR-1"), cancellationToken: Ct);
        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(rita, "PR-2"), cancellationToken: Ct);
        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(sam, "PR-3"), cancellationToken: Ct);

        using var ritaClient = host.Client("demo", host.Token("demo", rita));
        using var samClient = host.Client("demo", host.Token("demo", sam));
        var ritaPage = await WaitForListAsync(ritaClient, 2);
        await WaitForListAsync(samClient, 1);
        ritaPage.GetProperty("items").GetArrayLength().ShouldBe(2);
        (await WaitForListAsync(samClient, 1)).GetProperty("items").GetArrayLength().ShouldBe(1);

        // Sam can't mark Rita's notification as read: to Sam it doesn't exist.
        var ritaFirst = ritaPage.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await samClient.PostAsync(new Uri($"/api/notification/v1/notifications/{ritaFirst}/read", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);

        (await ritaClient.PostAsync(new Uri($"/api/notification/v1/notifications/{ritaFirst}/read", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);
        (await ritaClient.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications/unread-count", UriKind.Relative), Json, Ct)).GetProperty("count").GetInt32().ShouldBe(1);

        var unreadOnly = await ritaClient.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications?unread=true", UriKind.Relative), Json, Ct);
        unreadOnly.GetProperty("items").GetArrayLength().ShouldBe(1);

        var all = await ritaClient.PostAsync(new Uri("/api/notification/v1/notifications/read-all", UriKind.Relative), null, Ct);
        all.EnsureSuccessStatusCode();
        (await ritaClient.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications/unread-count", UriKind.Relative), Json, Ct)).GetProperty("count").GetInt32().ShouldBe(0);
        (await samClient.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications/unread-count", UriKind.Relative), Json, Ct)).GetProperty("count").GetInt32().ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_notification_is_pushed_to_the_open_session_of_the_right_person(string provider)
    {
        var rita = Guid.NewGuid();
        var sam = Guid.NewGuid();
        var contacts = new FakeContacts();
        contacts.People[rita] = new Contact(rita, "Rita", null);
        contacts.People[sam] = new Contact(sam, "Sam", null);
        await using var host = await StartAsync(provider, contacts, "demo");

        await using var ritaHub = Connect(host, host.Token("demo", rita));
        await using var samHub = Connect(host, host.Token("demo", sam));
        var ritaGot = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var samGot = new TaskCompletionSource<NotificationDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        ritaHub.On<NotificationDto>(Acme.Notification.Api.NotificationHub.PushMethod, ritaGot.SetResult);
        samHub.On<NotificationDto>(Acme.Notification.Api.NotificationHub.PushMethod, samGot.SetResult);
        await ritaHub.StartAsync(Ct);
        await samHub.StartAsync(Ct);

        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(rita, "PR-PUSH"), cancellationToken: Ct);

        var pushed = await ritaGot.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        pushed.Title.ShouldContain("PR-PUSH");
        samGot.Task.IsCompleted.ShouldBeFalse("a push goes only to its recipient");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_reminder_goes_to_everyone_who_can_approve_in_the_tenants_language(string provider)
    {
        var approver = Guid.NewGuid();
        var bystander = Guid.NewGuid();
        var contacts = new FakeContacts();
        contacts.People[approver] = new Contact(approver, "Approver Amy", null);
        contacts.People[bystander] = new Contact(bystander, "Bystander Bob", null);
        await using var host = await StartAsync(provider, contacts, "demo");

        await EventSender.SendAsync(databases, host.Tenants["demo"].Id,
            new ApprovalsWaiting.V1(2, new DateOnly(2026, 10, 1), ["PR-A", "PR-B"]), cancellationToken: Ct);

        using var approverClient = host.Client("demo", host.Token("demo", approver));
        var page = await WaitForListAsync(approverClient, 1);
        page.GetProperty("items")[0].GetProperty("kind").GetString().ShouldBe("purchase-request.approvals-waiting");
        page.GetProperty("items")[0].GetProperty("body").GetString()!.ShouldContain("PR-A, PR-B");

        using var bystanderClient = host.Client("demo", host.Token("demo", bystander));
        var none = await bystanderClient.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications", UriKind.Relative), Json, Ct);
        none.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Notifications_stay_inside_their_tenant(string provider)
    {
        var rita = Guid.NewGuid();   // the same person ID in both tenants is the worst case
        var contacts = new FakeContacts();
        contacts.People[rita] = new Contact(rita, "Rita", null);
        await using var host = await StartAsync(provider, contacts, "demo", "other");

        await EventSender.SendAsync(databases, host.Tenants["demo"].Id, Approved(rita, "PR-DEMO"), cancellationToken: Ct);

        using var demo = host.Client("demo", host.Token("demo", rita));
        await WaitForListAsync(demo, 1);
        using var other = host.Client("other", host.Token("other", rita));
        var page = await other.GetFromJsonAsync<JsonElement>(new Uri("/api/notification/v1/notifications", UriKind.Relative), Json, Ct);
        page.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private static HubConnection Connect(ModuleTestHost host, string token) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri("https://demo.localhost/api/notification/hub"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.Handler;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();
}
