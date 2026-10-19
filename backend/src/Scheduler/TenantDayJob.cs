using Saaskit.Contracts;
using Saaskit.Core.Messaging;
using Saaskit.Core.Tenancy;
using Saaskit.Core.Time;
using Quartz;

namespace Saaskit.Scheduler;

/// <summary>
/// Every 15 minutes, asks each active tenant "is it 09:00 where you are?" and, for the tenants where it is, sends
/// <see cref="TenantDayStarted.V1"/>. Modules with daily work consume it in the tenant's own scope, so the scheduler never
/// touches tenant data. The event ID is derived from tenant and local date: a misfire or a second firing produces the same message,
/// which the consumers' inbox handles once.
/// </summary>
[DisallowConcurrentExecution]
internal sealed partial class TenantDayJob(
    ITenantDirectory directory,
    IMessageBroker broker,
    TimeProvider time,
    ILogger<TenantDayJob> logger) : IJob
{
    public const string Name = "tenant-day-started";

    /// <summary>The local time of day the event goes out.</summary>
    public static readonly TimeOnly LocalTime = new(9, 0);

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken) => new(RunAsync(cancellationToken));

    /// <summary>One tick. Separate from <see cref="Execute"/> so tests can run it without a Quartz context.</summary>
    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var tenants = await directory.ListAsync(cancellationToken);
        var sent = 0;
        foreach (var tenant in tenants.Where(t => t.Status is TenantStatus.Active))
        {
            if (!TenantSchedule.IsDue(tenant.TimeZone, now, LocalTime, out var localDate))
                continue;

            var descriptor = EventRegistry.For<TenantDayStarted.V1>();
            var envelope = new EventEnvelope(
                TenantSchedule.EventId(Name, tenant.Id, localDate),
                descriptor.Name,
                descriptor.Version,
                tenant.Id,
                now.UtcDateTime,
                "scheduler",
                System.Diagnostics.Activity.Current?.Id,
                System.Text.Json.JsonSerializer.Serialize(new TenantDayStarted.V1(localDate), EventJson.Options));
            try
            {
                await broker.PublishAsync(envelope, cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One tenant's failure must not stop the others. The next tick is outside the window, so this tenant misses today's
                // event; the failure is logged for the operator.
                LogPublishFailed(logger, ex, tenant.Slug);
            }
        }
        LogTick(logger, sent);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not send the day-started event for {Tenant}")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, string tenant);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Day tick sent {Count} message(s)")]
    private static partial void LogTick(ILogger logger, int count);
}
