using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Acme.Core.Hosting.Messaging;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.PurchaseRequest;
using Acme.PurchaseRequest.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Role = Acme.Core.Platform.Role;

namespace Acme.Integration.Tests;

/// <summary>The reference module end to end, on both database providers: API, rules, numbering, files, outbox and tenant isolation.</summary>
[Trait("service", "purchase-request")]
[Collection(BrokerGroup.Name)]
public sealed class PurchaseRequestTests(Databases databases)
{
    private const string Api = "/api/purchase-request/v1";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly (string, Role)[] AsUser = [(Modules.PurchaseRequest, Role.User)];
    private static readonly (string, Role)[] AsManager = [(Modules.PurchaseRequest, Role.Manager)];
    private static readonly (string, Role)[] AsAdmin = [(Modules.PurchaseRequest, Role.Admin)];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static object Body(decimal price = 100m, decimal quantity = 2m, string title = "Printer paper") =>
        new { title, description = "For the office", lines = new[] { new { item = "A4 paper", quantity, unitPrice = price } } };

    private static Task<ModuleTestHost> StartAsync(Databases databases, string provider, params string[] tenants) =>
        ModuleTestHost.StartAsync<PurchaseRequestModule>(databases, provider, null, tenants.Length == 0 ? ["demo"] : tenants);

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(text);
        return JsonDocument.Parse(text).RootElement;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync($"{Api}/requests", body ?? Body(), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await ReadAsync(response);
    }

    private static string[] Actions(JsonElement request) => [.. request.GetProperty("allowedActions").EnumerateArray().Select(a => a.GetString()!)];

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_request_is_created_as_a_draft_numbered_for_the_tenants_business_day(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var user = host.Client("demo", host.Token("demo", roles: AsUser));

        var first = await CreateAsync(user);
        var second = await CreateAsync(user);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(TimeProvider.System.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(PlatformInfo.DefaultTimeZone)).DateTime);
        first.GetProperty("number").GetString().ShouldBe($"PR-{today:yyyyMMdd}-0001");
        second.GetProperty("number").GetString().ShouldBe($"PR-{today:yyyyMMdd}-0002");
        first.GetProperty("status").GetString().ShouldBe("draft");
        first.GetProperty("total").GetDecimal().ShouldBe(200m);
        first.GetProperty("currency").GetString().ShouldBe(PlatformInfo.DefaultCurrency);
        Actions(first).ShouldBe(["edit", "delete", "submit", "uploadQuote"], ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Concurrent_creates_never_share_a_number(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var user = host.Client("demo", host.Token("demo", roles: AsUser));

        var created = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => user.PostAsJsonAsync($"{Api}/requests", Body(), Ct)));
        created.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created);
        var numbers = new List<string>();
        foreach (var response in created)
            numbers.Add((await ReadAsync(response)).GetProperty("number").GetString()!);
        numbers.Distinct().Count().ShouldBe(20);
        numbers.Order().Select(n => int.Parse(n[^4..], System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(Enumerable.Range(1, 20));
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Invalid_input_is_refused_with_messages_in_the_callers_language(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var token = host.Token("demo", roles: AsUser);
        var bad = new { title = "", description = "x", lines = new[] { new { item = "", quantity = 0m, unitPrice = -1m } } };

        var english = await host.Client("demo", token).PostAsJsonAsync($"{Api}/requests", bad, Ct);
        english.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await english.Content.ReadAsStringAsync(Ct)).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("platform.validation");
        problem.GetProperty("errors").GetProperty("title")[0].GetString().ShouldBe("Title is required.");
        problem.GetProperty("errors").GetProperty("lines[0].quantity")[0].GetString().ShouldBe("Quantity must be greater than zero.");

        var malay = JsonDocument.Parse(await (await host.Client("demo", token, "ms").PostAsJsonAsync($"{Api}/requests", bad, Ct)).Content.ReadAsStringAsync(Ct)).RootElement;
        malay.GetProperty("errors").GetProperty("title")[0].GetString().ShouldBe("Title diperlukan.");

        var chinese = JsonDocument.Parse(await (await host.Client("demo", token, "zh-CN,zh;q=0.9").PostAsJsonAsync($"{Api}/requests", bad, Ct)).Content.ReadAsStringAsync(Ct)).RootElement;
        chinese.GetProperty("errors").GetProperty("title")[0].GetString().ShouldBe("Title为必填项。");
        chinese.GetProperty("message").GetString().ShouldBe("请求有问题。请检查各字段后重试。");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_repeated_Idempotency_Key_returns_the_original_and_never_creates_a_duplicate(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var user = host.Client("demo", host.Token("demo", roles: AsUser));

        HttpRequestMessage Post(object body, string key)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/requests") { Content = JsonContent.Create(body) };
            request.Headers.Add("Idempotency-Key", key);
            return request;
        }

        var first = await user.SendAsync(Post(Body(), "key-1"), Ct);
        var again = await user.SendAsync(Post(Body(), "key-1"), Ct);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        again.Headers.Location.ShouldBe(first.Headers.Location);
        (await ReadAsync(again)).GetProperty("id").GetGuid().ShouldBe((await ReadAsync(first)).GetProperty("id").GetGuid());

        // The same key with a different body is a mistake, not a retry.
        var different = await user.SendAsync(Post(Body(price: 999m), "key-1"), Ct);
        different.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadAsync2(different)).GetProperty("code").GetString().ShouldBe("platform.idempotency-key-reused");

        // Racing retries: still one request.
        var racing = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => user.SendAsync(Post(Body(price: 7m), "key-race"), Ct)));
        racing.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBeGreaterThanOrEqualTo(1);
        racing.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);

        var total = await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().Requests.CountAsync(Ct));
        total.ShouldBe(2);   // one for key-1, one for key-race
    }

    private static async Task<JsonElement> ReadAsync2(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement;

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Updates_need_the_current_version_via_If_Match(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var created = await CreateAsync(user);
        var id = created.GetProperty("id").GetGuid();
        var etag = (await user.GetAsync($"{Api}/requests/{id}", Ct)).Headers.ETag!.Tag;

        // No If-Match: 428. A stale version: 412. The current version: accepted, and the version changes.
        (await user.PutAsJsonAsync($"{Api}/requests/{id}", Body(title: "Changed"), Ct)).StatusCode.ShouldBe((HttpStatusCode)428);

        HttpRequestMessage Put(string ifMatch, string title)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, $"{Api}/requests/{id}") { Content = JsonContent.Create(Body(title: title)) };
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            return request;
        }

        var updated = await user.SendAsync(Put(etag, "Second title"), Ct);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(Ct));
        updated.Headers.ETag!.Tag.ShouldNotBe(etag);
        (await user.SendAsync(Put(etag, "Third title"), Ct)).StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Small_requests_are_approved_on_submit_and_larger_ones_wait(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var requester = host.Client("demo", host.Token("demo", roles: AsUser));

        var small = await SubmitAsync(requester, Body(price: 100m, quantity: 3m));   // 300 < the 500 default
        small.GetProperty("status").GetString().ShouldBe("approved");
        small.GetProperty("autoApproved").GetBoolean().ShouldBeTrue();
        Actions(small).ShouldBeEmpty();

        var large = await SubmitAsync(requester, Body(price: 100m, quantity: 6m));   // 600
        large.GetProperty("status").GetString().ShouldBe("submitted");
        large.GetProperty("autoApproved").GetBoolean().ShouldBeFalse();
    }

    private static async Task<JsonElement> SubmitAsync(HttpClient client, object body)
    {
        var created = await CreateAsync(client, body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Api}/requests/{created.GetProperty("id").GetGuid()}/submit");
        request.Headers.TryAddWithoutValidation("If-Match", created.GetProperty("eTag").GetString());
        return await ReadAsync(await client.SendAsync(request, Ct));
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, JsonElement request, string action, object? body = null)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, $"{Api}/requests/{request.GetProperty("id").GetGuid()}/{action}") { Content = body is null ? null : JsonContent.Create(body) };
        message.Headers.TryAddWithoutValidation("If-Match", request.GetProperty("eTag").GetString());
        return await client.SendAsync(message, Ct);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Managers_approve_and_the_buttons_follow_the_role_the_state_and_who_asks(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var requesterId = Guid.NewGuid();
        var requester = host.Client("demo", host.Token("demo", requesterId, roles: AsUser));
        var otherUser = host.Client("demo", host.Token("demo", roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));
        var managerWhoRequests = host.Client("demo", host.Token("demo", Guid.NewGuid(), roles: AsManager));

        var submitted = await SubmitAsync(requester, Body(price: 1000m));
        var id = submitted.GetProperty("id").GetGuid();

        // What each caller may do is decided on the server.
        Actions(await ReadAsync(await manager.GetAsync($"{Api}/requests/{id}", Ct))).ShouldBe(["approve", "reject"], ignoreOrder: true);
        Actions(await ReadAsync(await requester.GetAsync($"{Api}/requests/{id}", Ct))).ShouldBeEmpty();
        (await otherUser.GetAsync($"{Api}/requests/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);   // not theirs: not even visible

        // A user can't approve; the requester can't approve their own request even as a manager.
        (await PostActionAsync(requester, submitted, "approve")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var ownManager = host.Client("demo", host.Token("demo", requesterId, roles: AsManager));
        var own = await PostActionAsync(ownManager, submitted, "approve");
        own.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync2(own)).GetProperty("code").GetString().ShouldBe("purchase-request.own-request");

        var approved = await PostActionAsync(manager, submitted, "approve");
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync(Ct));
        (await ReadAsync(approved)).GetProperty("status").GetString().ShouldBe("approved");

        // It is decided; nobody can approve or reject it again.
        var stale = await ReadAsync(await manager.GetAsync($"{Api}/requests/{id}", Ct));
        (await PostActionAsync(managerWhoRequests, stale, "approve")).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await PostActionAsync(manager, stale, "reject", new { reason = "too late" })).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Rejecting_needs_a_reason_and_users_see_only_their_own_requests(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var alice = host.Client("demo", host.Token("demo", Guid.NewGuid(), roles: AsUser));
        var bob = host.Client("demo", host.Token("demo", Guid.NewGuid(), roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));

        var request = await SubmitAsync(alice, Body(price: 1000m));
        await CreateAsync(bob);

        (await PostActionAsync(manager, request, "reject", new { reason = "" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await ReadAsync(await PostActionAsync(manager, request, "reject", new { reason = "Over budget" }));
        rejected.GetProperty("status").GetString().ShouldBe("rejected");
        rejected.GetProperty("rejectionReason").GetString().ShouldBe("Over budget");

        (await ReadAsync(await alice.GetAsync($"{Api}/requests", Ct))).GetProperty("totalCount").GetInt32().ShouldBe(1);
        (await ReadAsync(await manager.GetAsync($"{Api}/requests", Ct))).GetProperty("totalCount").GetInt32().ShouldBe(2);
        var paged = await ReadAsync(await manager.GetAsync($"{Api}/requests?page=1&pageSize=1000", Ct));
        paged.GetProperty("pageSize").GetInt32().ShouldBe(100);   // a hard maximum
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Quotes_are_checked_by_their_real_content_and_downloaded_by_signed_link(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var user = host.Client("demo", host.Token("demo", roles: AsUser));
        var created = await CreateAsync(user);
        var id = created.GetProperty("id").GetGuid();

        async Task<HttpResponseMessage> Upload(byte[] bytes, string name, JsonElement request)
        {
            var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", name } };
            var message = new HttpRequestMessage(HttpMethod.Post, $"{Api}/requests/{id}/quote") { Content = form };
            message.Headers.TryAddWithoutValidation("If-Match", request.GetProperty("eTag").GetString());
            return await user.SendAsync(message, Ct);
        }

        // A program renamed to .pdf, and a png claiming to be a pdf, are refused: the bytes decide.
        var exe = Encoding.ASCII.GetBytes("MZ\u0090\u0000\u0003\u0000\u0000\u0000").Concat(new byte[200]).ToArray();
        (await Upload(exe, "quote.pdf", created)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[50]).ToArray();
        (await Upload(png, "evil.exe", created)).StatusCode.ShouldBe(HttpStatusCode.OK);   // a real png is fine whatever its name

        var current = await ReadAsync(await user.GetAsync($"{Api}/requests/{id}", Ct));
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%quote body\n");
        var attached = await Upload(pdf, "../../etc/passwd.pdf", current);
        attached.StatusCode.ShouldBe(HttpStatusCode.OK, await attached.Content.ReadAsStringAsync(Ct));
        var fileId = (await ReadAsync(attached)).GetProperty("quoteFileId").GetGuid();

        // The stored key is server-generated: {tenantId}/{module}/{entity}/{id}/{fileId}, never the client's name.
        var stored = await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().StoredFiles.AsNoTracking().SingleAsync(f => f.Id == fileId, Ct));
        stored.Key.ShouldStartWith($"{host.Tenants["demo"].Id}/{Modules.PurchaseRequest}/quote/{id}/");
        Guid.TryParse(stored.Key[(stored.Key.LastIndexOf('/') + 1)..], out _).ShouldBeTrue();   // a server-generated file ID, never the client's name
        stored.DisplayName.ShouldBe("passwd.pdf");
        stored.ContentType.ShouldBe("application/pdf");

        var link = (await ReadAsync(await user.GetAsync($"{Api}/requests/{id}/quote", Ct))).GetProperty("url").GetString()!;
        var download = await host.Client("demo").GetAsync(link, Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(pdf);
        (await host.Client("demo").GetAsync("/files/local/not-a-valid-token", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Only_machines_confirm_goods_received_and_only_for_approved_requests(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        var requester = host.Client("demo", host.Token("demo", roles: AsUser));
        var approved = await SubmitAsync(requester, Body(price: 10m, quantity: 1m));   // auto-approved
        var waiting = await SubmitAsync(requester, Body(price: 1000m));
        var machine = host.Client("demo", host.Token("demo", type: PrincipalType.Machine, roles: AsUser));
        var person = host.Client("demo", host.Token("demo", type: PrincipalType.User, roles: AsAdmin));

        var received = await machine.PostAsJsonAsync($"{Api}/goods-received", new { requestId = approved.GetProperty("id").GetGuid() }, Ct);
        received.StatusCode.ShouldBe(HttpStatusCode.OK, await received.Content.ReadAsStringAsync(Ct));
        (await ReadAsync(received)).GetProperty("goodsReceivedOn").ValueKind.ShouldNotBe(JsonValueKind.Null);

        (await person.PostAsJsonAsync($"{Api}/goods-received", new { requestId = approved.GetProperty("id").GetGuid() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await machine.PostAsJsonAsync($"{Api}/goods-received", new { requestId = waiting.GetProperty("id").GetGuid() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Tenants_are_isolated_and_suspended_tenants_are_read_only(string provider)
    {
        await using var host = await StartAsync(databases, provider, "demo", "demo-plus");
        var demo = host.Client("demo", host.Token("demo", roles: AsAdmin));
        var plus = host.Client("demo-plus", host.Token("demo-plus", roles: AsAdmin));

        var theirs = await CreateAsync(demo);
        (await plus.GetAsync($"{Api}/requests/{theirs.GetProperty("id").GetGuid()}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(await plus.GetAsync($"{Api}/requests", Ct))).GetProperty("totalCount").GetInt32().ShouldBe(0);

        // Each tenant has its own database: the row exists in one and not the other.
        (await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().Requests.CountAsync(Ct))).ShouldBe(1);
        (await host.InTenantAsync("demo-plus", sp => sp.GetRequiredService<PurchaseRequestDbContext>().Requests.CountAsync(Ct))).ShouldBe(0);

        // A token for demo is useless on demo-plus's host.
        var crossed = host.Client("demo-plus", host.Token("demo", roles: AsAdmin));
        (await crossed.GetAsync($"{Api}/requests", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await host.InTenantAsync("demo", async sp =>
        {
            var catalog = sp.GetRequiredService<Acme.Core.Catalog.CatalogDbContext>();
            (await catalog.Tenants.SingleAsync(t => t.Slug == "demo", Ct)).ChangeStatus(Acme.Core.Tenancy.TenantStatus.Suspended);
            await catalog.SaveChangesAsync(Ct);
            sp.GetRequiredService<Acme.Core.Tenancy.ITenantDirectory>().Invalidate(host.Tenants["demo"].Id);
        });
        (await demo.GetAsync($"{Api}/requests", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var write = await demo.PostAsJsonAsync($"{Api}/requests", Body(), Ct);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadAsync2(write)).GetProperty("code").GetString().ShouldBe("platform.tenant-read-only");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Approval_publishes_one_event_through_the_outbox_to_RabbitMQ_with_the_tenant_and_the_trace(string provider)
    {
        await using var host = await StartAsync(databases, provider);
        await using var probe = await EventProbe.StartAsync(databases, "purchase-request.approved.v1");
        var requester = host.Client("demo", host.Token("demo", roles: AsUser));
        var manager = host.Client("demo", host.Token("demo", roles: AsManager));

        var submitted = await SubmitAsync(requester, Body(price: 1000m));
        (await PostActionAsync(manager, submitted, "approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var message = await probe.NextAsync(TimeSpan.FromSeconds(20)) ?? throw new TimeoutException("No event arrived.");
        message.Headers["x-tenant-id"].ShouldBe(host.Tenants["demo"].Id.ToString());
        message.Headers["x-producer"].ShouldBe(PlatformServices.PurchaseRequest.Schema);
        message.Headers["traceparent"].ShouldStartWith("00-");
        var payload = JsonDocument.Parse(message.Body).RootElement;
        payload.GetProperty("requestId").GetGuid().ShouldBe(submitted.GetProperty("id").GetGuid());
        payload.GetProperty("total").GetDecimal().ShouldBe(2000m);
        payload.GetProperty("autoApproved").GetBoolean().ShouldBeFalse();

        // Published exactly once, and marked as published in the outbox.
        (await probe.NextAsync(TimeSpan.FromSeconds(2))).ShouldBeNull();
        var pending = await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().OutboxMessages.CountAsync(o => o.PublishedOn == null, Ct));
        pending.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_lost_signal_is_caught_by_the_sweep_and_a_rolled_back_change_publishes_nothing(string provider)
    {
        // The signal is dropped (Redis stream write fails), so only the sweep can publish.
        await using var host = await ModuleTestHost.StartAsync<PurchaseRequestModule>(
            databases, provider, builder => builder.Services.AddSingleton<Acme.Core.Messaging.IOutboxSignal, DroppedSignal>(), "demo");
        await using var probe = await EventProbe.StartAsync(databases, "purchase-request.submitted.v1");
        var requester = host.Client("demo", host.Token("demo", roles: AsUser));

        await SubmitAsync(requester, Body(price: 1000m));
        (await probe.NextAsync(TimeSpan.FromSeconds(3))).ShouldBeNull("without a signal nothing is published until the sweep");
        (await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().OutboxMessages.CountAsync(o => o.PublishedOn == null, Ct))).ShouldBe(1);

        await host.SweepOutboxAsync();
        (await probe.NextAsync(TimeSpan.FromSeconds(15))).ShouldNotBeNull();

        // A failed request saves nothing, so nothing is published: validation fails before the transaction.
        (await requester.PostAsJsonAsync($"{Api}/requests", new { title = "", lines = Array.Empty<object>() }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().OutboxMessages.CountAsync(Ct))).ShouldBe(1);
    }

    private sealed class DroppedSignal : Acme.Core.Messaging.IOutboxSignal
    {
        public Task SignalAsync(Guid tenantId, string schema, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the signal store is unavailable");
    }
}

/// <summary>A queue bound to the event exchange, to watch what the platform publishes.</summary>
internal sealed class EventProbe : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly string _queue;

    private EventProbe(IConnection connection, IChannel channel, string queue)
    {
        _connection = connection;
        _channel = channel;
        _queue = queue;
    }

    public static async Task<EventProbe> StartAsync(Databases databases, string routingKey)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(databases.RabbitMqConnectionString) }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync();
        await RabbitMqConnection.DeclareExchangesAsync(channel, CancellationToken.None);
        var queue = (await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true)).QueueName;
        await channel.QueueBindAsync(queue, Platform.EventExchange, routingKey);
        return new EventProbe(connection, channel, queue);
    }

    public async Task<ProbeMessage?> NextAsync(TimeSpan timeout)
    {
        var deadline = TimeProvider.System.GetUtcNow() + timeout;
        while (TimeProvider.System.GetUtcNow() < deadline)
        {
            var result = await _channel.BasicGetAsync(_queue, autoAck: true);
            if (result is not null)
            {
                var headers = (result.BasicProperties.Headers ?? new Dictionary<string, object?>())
                    .ToDictionary(h => h.Key, h => h.Value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : h.Value?.ToString() ?? "");
                return new ProbeMessage(headers, Encoding.UTF8.GetString(result.Body.Span), result.BasicProperties.MessageId);
            }
            await Task.Delay(100);
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static class Platform
    {
        public static readonly string EventExchange = $"{Acme.Core.Platform.PlatformInfo.Product}.events";
    }
}

internal sealed record ProbeMessage(IReadOnlyDictionary<string, string> Headers, string Body, string? MessageId);
