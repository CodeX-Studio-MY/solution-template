using Acme.Contracts;
using Acme.Core.Configuration;
using Acme.Core.Data.PostgreSql;
using Acme.Core.Messaging;
using Acme.Core.Tenancy;
using Acme.Core.Time;
using Acme.Migrator;
using Acme.Scheduler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Quartz;

namespace Acme.Integration.Tests;

/// <summary>The Scheduler: tenant-local timing of the reminder, and the clustered Quartz store created by the Migrator on both providers.</summary>
[Trait("service", "scheduler")]
public sealed class SchedulerTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeDirectory(params TenantInfo[] tenants) : ITenantDirectory
    {
        public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(tenants.FirstOrDefault(t => t.Id == tenantId));

        public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default) => Task.FromResult(tenants.FirstOrDefault(t => t.Slug == slug));

        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TenantInfo>>(tenants);

        public void Invalidate(Guid tenantId)
        {
        }
    }

    private sealed class RecordingBroker : IMessageBroker
    {
        public List<EventEnvelope> Sent { get; } = [];

        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    private static TenantInfo Tenant(string slug, string zone, TenantStatus status = TenantStatus.Active) =>
        new(Guid.NewGuid(), slug, slug, status, "standard", zone, "MYR", "en", new HashSet<string>(StringComparer.Ordinal), new TenantPlacement("fake", "default", $"t_{slug}", null));

    [Fact]
    public async Task Only_active_tenants_where_it_is_nine_get_the_reminder_with_a_stable_ID()
    {
        var kl = Tenant("kl", "Asia/Kuala_Lumpur");
        var ny = Tenant("ny", "America/New_York");
        var suspended = Tenant("suspended", "Asia/Kuala_Lumpur", TenantStatus.Suspended);
        var broker = new RecordingBroker();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 1, 0, 0, TimeSpan.Zero));   // 09:00 in KL, 21:00 the day before in New York
        var job = new TenantReminderJob(new FakeDirectory(kl, ny, suspended), broker, time, NullLogger<TenantReminderJob>.Instance);

        await job.RunAsync(Ct);

        var sent = broker.Sent.ShouldHaveSingleItem();
        sent.TenantId.ShouldBe(kl.Id);
        sent.RoutingKey.ShouldBe(EventRegistry.For<ApprovalReminderDue.V1>().RoutingKey);
        sent.EventId.ShouldBe(TenantSchedule.EventId(TenantReminderJob.Name, kl.Id, new DateOnly(2026, 10, 9)));
        System.Text.Json.JsonSerializer.Deserialize<ApprovalReminderDue.V1>(sent.Payload, EventJson.Options)!.BusinessDate.ShouldBe(new DateOnly(2026, 10, 9));

        // The same tick again (a misfire, a second instance) sends the same message ID, which the consumers' inbox handles once.
        await job.RunAsync(Ct);
        broker.Sent.Select(e => e.EventId).Distinct().Count().ShouldBe(1);

        // Fifteen minutes later nobody is due.
        time.Advance(TimeSpan.FromMinutes(15));
        broker.Sent.Clear();
        await job.RunAsync(Ct);
        broker.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_tenants_publish_failure_does_not_stop_the_others()
    {
        var first = Tenant("first", "Asia/Kuala_Lumpur");
        var second = Tenant("second", "Asia/Kuala_Lumpur");
        var broker = new FailingOnceBroker(first.Id);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 9, 1, 5, 0, TimeSpan.Zero));
        var job = new TenantReminderJob(new FakeDirectory(first, second), broker, time, NullLogger<TenantReminderJob>.Instance);

        await job.RunAsync(Ct);

        broker.Sent.Select(e => e.TenantId).ShouldBe([second.Id]);
    }

    private sealed class FailingOnceBroker(Guid failing) : IMessageBroker
    {
        public List<EventEnvelope> Sent { get; } = [];

        public Task PublishAsync(EventEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.TenantId == failing)
                throw new InvalidOperationException("The broker is down for this one.");
            Sent.Add(envelope);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task The_migrated_Quartz_store_runs_a_cluster_that_keeps_one_copy_of_the_job(string provider)
    {
        var dbProvider = Databases.Provider(provider);
        var catalog = databases.NewDatabase(provider, "catalog");
        foreach (var target in MigrationTargets.Catalog)
        {
            await using var db = target.Create(dbProvider, catalog);
            await db.Database.MigrateAsync(Ct);
        }

        var options = new DatabaseOptions { Provider = provider, CatalogConnectionString = catalog };
        using var first = await StartSchedulerAsync(options);
        using var second = await StartSchedulerAsync(options);   // a second instance on the same store
        try
        {
            var firstScheduler = await first.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);
            var secondScheduler = await second.Services.GetRequiredService<ISchedulerFactory>().GetScheduler(Ct);

            firstScheduler.SchedulerInstanceId.ShouldNotBe(secondScheduler.SchedulerInstanceId);
            (await firstScheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup(), Ct)).Count.ShouldBe(1);
            var trigger = await secondScheduler.GetTrigger(new TriggerKey($"{TenantReminderJob.Name}-tick"), Ct);
            trigger.ShouldNotBeNull();
            (await secondScheduler.GetTriggerState(trigger.Key, Ct)).ShouldBe(TriggerState.Normal);
            ((ICronTrigger)trigger).CronExpressionString.ShouldBe("0 0/15 * * * ?");
        }
        finally
        {
            // Quartz's threads must be gone before the database is.
            await first.StopAsync(Ct);
            await second.StopAsync(Ct);
        }
    }

    private static async Task<IHost> StartSchedulerAsync(DatabaseOptions database)
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddPlatformScheduler(database).AddSingleton<ITenantDirectory>(new FakeDirectory()).AddSingleton<IMessageBroker>(new RecordingBroker()))
            .Build();
        await host.StartAsync(Ct);
        return host;
    }

    [Fact]
    public void The_provider_name_constant_matches_the_options()
    {
        // The scheduler picks its Quartz driver from Database:Provider; a rename must not silently fall back to SQL Server.
        PostgreSqlDatabaseProvider.ProviderName.ShouldBe("postgresql");
    }
}
