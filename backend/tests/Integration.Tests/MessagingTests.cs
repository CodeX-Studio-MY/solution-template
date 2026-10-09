using System.Diagnostics;
using System.Text;
using Acme.Contracts;
using Acme.Core.Catalog;
using Acme.Core.Hosting.Messaging;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Acme.Core.Tenancy;
using Acme.PurchaseRequest;
using Acme.PurchaseRequest.Domain;
using Acme.PurchaseRequest.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Acme.Integration.Tests;

/// <summary>The messaging layer over RabbitMQ.Client: the inbox, entitlement filtering, retries, dead-letter queues and tracing.</summary>
[Trait("service", "purchase-request")]
[Collection(BrokerGroup.Name)]
public sealed class MessagingTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string ReminderKey = EventRegistry.For<ApprovalReminderDue.V1>().RoutingKey;
    private static readonly string WaitingKey = EventRegistry.For<ApprovalsWaiting.V1>().RoutingKey;

    /// <summary>Sends an event to the exchange the way the Scheduler (or any producer) would.</summary>
    private async Task SendAsync(Guid tenantId, Guid eventId, string? traceParent = null, string? payload = null)
    {
        await using var connection = await new ConnectionFactory { Uri = new Uri(databases.RabbitMqConnectionString) }.CreateConnectionAsync(Ct);
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Ct);
        await RabbitMqConnection.DeclareExchangesAsync(channel, Ct);
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = eventId.ToString(),
            Type = ReminderKey,
            Headers = new Dictionary<string, object?>
            {
                ["x-tenant-id"] = tenantId.ToString(),
                ["x-producer"] = "scheduler",
                ["traceparent"] = traceParent,
            },
        };
        var body = payload ?? $$"""{"businessDate":"{{DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().Date):yyyy-MM-dd}}"}""";
        await channel.BasicPublishAsync($"{PlatformInfo.Product}.events", ReminderKey, mandatory: false, properties, Encoding.UTF8.GetBytes(body), Ct);
    }

    private static async Task SeedOldSubmittedRequestAsync(ModuleTestHost host, string slug, int daysAgo)
    {
        await host.InTenantAsync(slug, async sp =>
        {
            var db = sp.GetRequiredService<PurchaseRequestDbContext>();
            var request = new Acme.PurchaseRequest.Domain.PurchaseRequest("PR-OLD-0001", Guid.NewGuid(), "Waiting", null, new DateOnly(2026, 1, 1), "MYR");
            request.ReplaceDetails("Waiting", null, [new RequestLine("Thing", 1, 1000m)]);
            request.Submit(TimeProvider.System.GetUtcNow().UtcDateTime.AddDays(-daysAgo));
            db.Requests.Add(request);
            await db.SaveChangesAsync(Ct);
        });
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_duplicate_delivery_is_handled_once_and_the_tenant_is_restored(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<PurchaseRequestModule>(databases, provider, null, "demo");
        await using var probe = await EventProbe.StartAsync(databases, WaitingKey);
        await SeedOldSubmittedRequestAsync(host, "demo", daysAgo: 3);

        var eventId = Guid.NewGuid();
        await SendAsync(host.Tenants["demo"].Id, eventId);
        await SendAsync(host.Tenants["demo"].Id, eventId);   // the broker delivered it twice

        var message = await probe.NextAsync(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("The consumer produced nothing.");
        message.Headers["x-tenant-id"].ShouldBe(host.Tenants["demo"].Id.ToString());
        using var payload = System.Text.Json.JsonDocument.Parse(message.Body);
        payload.RootElement.GetProperty("count").GetInt32().ShouldBe(1);
        payload.RootElement.GetProperty("numbers")[0].GetString().ShouldBe("PR-OLD-0001");

        // Exactly one result, and one inbox row for the consumer.
        (await probe.NextAsync(TimeSpan.FromSeconds(3))).ShouldBeNull();
        var inbox = await host.InTenantAsync("demo", sp => sp.GetRequiredService<PurchaseRequestDbContext>().InboxMessages.CountAsync(i => i.EventId == eventId, Ct));
        inbox.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Events_reach_a_module_only_for_tenants_entitled_to_it(string provider)
    {
        await using var host = await ModuleTestHost.StartAsync<PurchaseRequestModule>(databases, provider, null, "demo", "no-module");
        await using var probe = await EventProbe.StartAsync(databases, WaitingKey);
        await SeedOldSubmittedRequestAsync(host, "demo", 3);
        await SeedOldSubmittedRequestAsync(host, "no-module", 3);

        // "no-module" loses the purchase-request entitlement.
        await host.InTenantAsync("no-module", async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogDbContext>();
            var moduleId = Modules.All.Single(m => m.Key == Modules.PurchaseRequest).Id;
            catalog.TenantModules.RemoveRange(catalog.TenantModules.Where(t => t.TenantId == host.Tenants["no-module"].Id && t.ModuleId == moduleId));
            await catalog.SaveChangesAsync(Ct);
            sp.GetRequiredService<ITenantDirectory>().Invalidate(host.Tenants["no-module"].Id);
        });

        await SendAsync(host.Tenants["no-module"].Id, Guid.NewGuid());
        await SendAsync(host.Tenants["demo"].Id, Guid.NewGuid());

        var message = await probe.NextAsync(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException();
        message.Headers["x-tenant-id"].ShouldBe(host.Tenants["demo"].Id.ToString());
        (await probe.NextAsync(TimeSpan.FromSeconds(3))).ShouldBeNull("the tenant without the module got nothing");
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_failing_consumer_is_retried_then_dead_lettered_and_can_be_replayed(string provider)
    {
        FlakyConsumer.Reset();
        await using var host = await ModuleTestHost.StartAsync<PurchaseRequestModule>(databases, provider, builder =>
        {
            builder.Configuration["RabbitMq:MaxAttempts"] = "2";
            builder.Services.AddEventConsumer<ApprovalReminderDue.V1, FlakyConsumer, PurchaseRequestDbContext>(PlatformServices.PurchaseRequest, Modules.PurchaseRequest);
        }, "demo");

        var eventId = Guid.NewGuid();
        await SendAsync(host.Tenants["demo"].Id, eventId);

        // It fails, waits out the retry delay, fails again, and lands in the dead-letter queue with the error.
        var admin = host.Services.GetRequiredService<IDeadLetterAdmin>();
        DeadLetter? dead = null;
        for (var i = 0; i < 60 && dead is null; i++)
        {
            await Task.Delay(500, Ct);
            dead = (await admin.ListAsync(cancellationToken: Ct)).FirstOrDefault(d => d.Queue.Contains(nameof(FlakyConsumer), StringComparison.Ordinal));
        }
        dead.ShouldNotBeNull("the message never reached the dead-letter queue");
        dead.Error!.ShouldContain("planned failure");
        dead.Attempts.ShouldBe(2);
        dead.TenantId.ShouldBe(host.Tenants["demo"].Id);
        FlakyConsumer.Calls.ShouldBe(2);

        // The cause is fixed; replay moves it back and the consumer succeeds.
        FlakyConsumer.Fail = false;
        (await admin.ReplayAsync(dead.Queue, cancellationToken: Ct)).ShouldBe(1);
        for (var i = 0; i < 60 && FlakyConsumer.Succeeded == 0; i++)
            await Task.Delay(500, Ct);
        FlakyConsumer.Succeeded.ShouldBe(1);
        (await admin.ListAsync(cancellationToken: Ct)).ShouldNotContain(d => d.Queue.Contains(nameof(FlakyConsumer), StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_consumer_runs_for_the_messages_tenant_and_continues_the_producers_trace(string provider)
    {
        TraceConsumer.Reset();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Acme.Core.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var host = await ModuleTestHost.StartAsync<PurchaseRequestModule>(databases, provider, builder =>
            builder.Services.AddEventConsumer<ApprovalReminderDue.V1, TraceConsumer, PurchaseRequestDbContext>(PlatformServices.PurchaseRequest, Modules.PurchaseRequest), "demo", "demo-plus");

        var traceId = ActivityTraceId.CreateRandom();
        var traceParent = $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01";
        await SendAsync(host.Tenants["demo-plus"].Id, Guid.NewGuid(), traceParent);

        for (var i = 0; i < 60 && TraceConsumer.Seen is null; i++)
            await Task.Delay(500, Ct);
        TraceConsumer.Seen.ShouldNotBeNull();
        TraceConsumer.Seen.Value.Tenant.ShouldBe(host.Tenants["demo-plus"].Id);
        TraceConsumer.Seen.Value.TraceId.ShouldBe(traceId.ToString());
    }

    /// <summary>Fails until told not to, so retries, the dead-letter queue and replay can be seen.</summary>
    internal sealed class FlakyConsumer : IEventConsumer<ApprovalReminderDue.V1>
    {
        public static bool Fail { get; set; } = true;

        public static int Calls { get; private set; }

        public static int Succeeded { get; private set; }

        public static void Reset()
        {
            Fail = true;
            Calls = 0;
            Succeeded = 0;
        }

        public Task HandleAsync(ApprovalReminderDue.V1 integrationEvent, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
                throw new InvalidOperationException("planned failure");
            Succeeded++;
            return Task.CompletedTask;
        }
    }

    internal sealed class TraceConsumer(ITenantContext tenant) : IEventConsumer<ApprovalReminderDue.V1>
    {
        public static (Guid Tenant, string TraceId)? Seen { get; private set; }

        public static void Reset() => Seen = null;

        public Task HandleAsync(ApprovalReminderDue.V1 integrationEvent, CancellationToken cancellationToken)
        {
            Seen = (tenant.Tenant.Id, Activity.Current?.TraceId.ToString() ?? "");
            return Task.CompletedTask;
        }
    }
}
