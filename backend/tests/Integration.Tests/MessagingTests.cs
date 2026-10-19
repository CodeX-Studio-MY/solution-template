using System.Diagnostics;
using System.Text;
using Saaskit.Contracts;
using Saaskit.Core.Catalog;
using Saaskit.Core.Hosting.Messaging;
using Saaskit.Core.Messaging;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Saaskit.Notification;
using Saaskit.Notification.Application;
using Saaskit.Notification.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RabbitMQ.Client;

namespace Saaskit.Integration.Tests;

/// <summary>The messaging layer over RabbitMQ.Client: the outbox, the inbox, entitlement filtering, retries, dead-letter queues and tracing.</summary>
[Trait("service", "notification")]
[Collection(BrokerGroup.Name)]
public sealed class MessagingTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string DayKey = EventRegistry.For<TenantDayStarted.V1>().RoutingKey;
    private static readonly string EchoKey = EventRegistry.For<NotificationRequested.V1>().RoutingKey;

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
            Type = DayKey,
            Headers = new Dictionary<string, object?>
            {
                ["x-tenant-id"] = tenantId.ToString(),
                ["x-producer"] = "scheduler",
                ["traceparent"] = traceParent,
            },
        };
        var body = payload ?? $$"""{"businessDate":"{{DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().Date):yyyy-MM-dd}}"}""";
        await channel.BasicPublishAsync($"{PlatformInfo.Product}.events", DayKey, mandatory: false, properties, Encoding.UTF8.GetBytes(body), Ct);
    }

    /// <summary>
    /// The Notification service with the fixture's <see cref="DayEchoConsumer"/>, owned by <see cref="Fixture.Optional"/> so entitlement
    /// applies, and nobody to notify.
    /// </summary>
    private Task<ModuleTestHost> StartAsync(string provider, Action<WebApplicationBuilder>? configure, params string[] tenants) =>
        ModuleTestHost.StartAsync<NotificationModule>(databases, provider, builder =>
        {
            // Required at startup; nothing is sent, as there is nobody to tell.
            builder.Configuration["Smtp:Host"] = "localhost";
            builder.Configuration["Smtp:FromAddress"] = "no-reply@saaskit.test";
            builder.Services.RemoveAll<IContactDirectory>();
            builder.Services.AddSingleton<IContactDirectory, NoContacts>();
            // The echo publishes, so the service gets the outbox a publishing module registers.
            builder.Services.AddModuleOutbox<NotificationDbContext>(Fixture.Service.Schema);
            builder.Services.AddEventConsumer<TenantDayStarted.V1, DayEchoConsumer, NotificationDbContext>(Fixture.Service, Fixture.Optional);
            configure?.Invoke(builder);
        }, tenants);

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_duplicate_delivery_is_handled_once_and_the_tenant_is_restored(string provider)
    {
        await using var host = await StartAsync(provider, null, "demo");
        await using var probe = await EventProbe.StartAsync(databases, EchoKey);

        var eventId = Guid.NewGuid();
        await SendAsync(host.Tenants["demo"].Id, eventId);
        await SendAsync(host.Tenants["demo"].Id, eventId);   // the broker delivered it twice

        var message = await probe.NextAsync(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("The consumer produced nothing.");
        message.Headers["x-tenant-id"].ShouldBe(host.Tenants["demo"].Id.ToString());
        using var payload = System.Text.Json.JsonDocument.Parse(message.Body);
        payload.RootElement.GetProperty("title").GetString()!.ShouldStartWith("demo ");

        // Exactly one result, and one inbox row for the consumer.
        (await probe.NextAsync(TimeSpan.FromSeconds(3))).ShouldBeNull();
        var inbox = await host.InTenantAsync("demo", sp => sp.GetRequiredService<NotificationDbContext>().InboxMessages.CountAsync(i => i.EventId == eventId, Ct));
        inbox.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task Events_reach_a_module_only_for_tenants_entitled_to_it(string provider)
    {
        await using var host = await StartAsync(provider, null, "demo", "no-module");
        await using var probe = await EventProbe.StartAsync(databases, EchoKey);

        // "no-module" loses the entitlement to the consumer's module.
        await host.InTenantAsync("no-module", async sp =>
        {
            var catalog = sp.GetRequiredService<CatalogDbContext>();
            var moduleId = Modules.All.Single(m => m.Key == Fixture.Optional).Id;
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
        await using var host = await StartAsync(provider, builder =>
        {
            builder.Configuration["RabbitMq:MaxAttempts"] = "2";
            builder.Services.AddEventConsumer<TenantDayStarted.V1, FlakyConsumer, NotificationDbContext>(Fixture.Service, Fixture.Entitled);
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
            ShouldListenTo = source => source.Name == "Saaskit.Core.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        await using var host = await StartAsync(provider, builder =>
            builder.Services.AddEventConsumer<TenantDayStarted.V1, TraceConsumer, NotificationDbContext>(Fixture.Service, Fixture.Entitled), "demo", "demo-plus");

        var traceId = ActivityTraceId.CreateRandom();
        var traceParent = $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01";
        await SendAsync(host.Tenants["demo-plus"].Id, Guid.NewGuid(), traceParent);

        for (var i = 0; i < 60 && TraceConsumer.Seen is null; i++)
            await Task.Delay(500, Ct);
        TraceConsumer.Seen.ShouldNotBeNull();
        TraceConsumer.Seen.Value.Tenant.ShouldBe(host.Tenants["demo-plus"].Id);
        TraceConsumer.Seen.Value.TraceId.ShouldBe(traceId.ToString());
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_consumers_event_goes_out_once_through_the_outbox_with_the_tenant_and_the_trace(string provider)
    {
        await using var host = await StartAsync(provider, null, "demo");
        await using var probe = await EventProbe.StartAsync(databases, EchoKey);

        await SendAsync(host.Tenants["demo"].Id, Guid.NewGuid(), $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01");

        var message = await probe.NextAsync(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("No event arrived.");
        message.Headers["x-tenant-id"].ShouldBe(host.Tenants["demo"].Id.ToString());
        message.Headers["x-producer"].ShouldBe(Fixture.Service.Schema);
        message.Headers["traceparent"].ShouldStartWith("00-");

        // Published exactly once, and marked as published in the outbox.
        (await probe.NextAsync(TimeSpan.FromSeconds(2))).ShouldBeNull();
        var pending = await host.InTenantAsync("demo", sp => sp.GetRequiredService<NotificationDbContext>().OutboxMessages.CountAsync(o => o.PublishedOn == null, Ct));
        pending.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_lost_signal_is_caught_by_the_sweep(string provider)
    {
        // The signal is dropped (Redis stream write fails), so only the sweep can publish.
        await using var host = await StartAsync(provider, builder => builder.Services.AddSingleton<IOutboxSignal, DroppedSignal>(), "demo");
        await using var probe = await EventProbe.StartAsync(databases, EchoKey);

        await SendAsync(host.Tenants["demo"].Id, Guid.NewGuid());
        for (var i = 0; i < 60; i++)
        {
            if (await host.InTenantAsync("demo", sp => sp.GetRequiredService<NotificationDbContext>().OutboxMessages.AnyAsync(Ct)))
                break;
            await Task.Delay(500, Ct);
        }
        (await probe.NextAsync(TimeSpan.FromSeconds(3))).ShouldBeNull("without a signal nothing is published until the sweep");
        (await host.InTenantAsync("demo", sp => sp.GetRequiredService<NotificationDbContext>().OutboxMessages.CountAsync(o => o.PublishedOn == null, Ct))).ShouldBe(1);

        await host.SweepOutboxAsync();
        (await probe.NextAsync(TimeSpan.FromSeconds(15))).ShouldNotBeNull();
    }

    private sealed class DroppedSignal : IOutboxSignal
    {
        public Task SignalAsync(Guid tenantId, string schema, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the signal store is unavailable");
    }

    /// <summary>Fails until told not to, so retries, the dead-letter queue and replay can be seen.</summary>
    internal sealed class FlakyConsumer : IEventConsumer<TenantDayStarted.V1>
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

        public Task HandleAsync(TenantDayStarted.V1 integrationEvent, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
                throw new InvalidOperationException("planned failure");
            Succeeded++;
            return Task.CompletedTask;
        }
    }

    internal sealed class TraceConsumer(ITenantContext tenant) : IEventConsumer<TenantDayStarted.V1>
    {
        public static (Guid Tenant, string TraceId)? Seen { get; private set; }

        public static void Reset() => Seen = null;

        public Task HandleAsync(TenantDayStarted.V1 integrationEvent, CancellationToken cancellationToken)
        {
            Seen = (tenant.Tenant.Id, Activity.Current?.TraceId.ToString() ?? "");
            return Task.CompletedTask;
        }
    }
}
