using Saaskit.Core.Configuration;
using Saaskit.Core.Messaging;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Saaskit.Core.Hosting.Messaging;

/// <summary>Signals "tenant X has pending outbox rows in schema S" on a Redis stream.</summary>
internal sealed class RedisOutboxSignal(IConnectionMultiplexer redis) : IOutboxSignal
{
    public static readonly RedisKey Stream = $"{PlatformInfo.Product}:outbox-signals";

    public async Task SignalAsync(Guid tenantId, string schema, CancellationToken cancellationToken = default) =>
        await redis.GetDatabase().StreamAddAsync(Stream, [new NameValueEntry("tenant", tenantId.ToString()), new NameValueEntry("schema", schema)], maxLength: 100_000, useApproximateMaxLength: true);
}

/// <summary>
/// Publishes outbox rows. It is signal-driven, not a poller over every tenant database: a committed write signals the tenant,
/// and workers take signals from a Redis consumer group, so only tenants with something to publish are touched. A slow sweep
/// over every tenant catches any signal that was lost, and trims old published rows.
/// </summary>
internal sealed partial class OutboxRelayService(
    IConnectionMultiplexer redis,
    IEnumerable<IOutboxSource> sources,
    ITenantDirectory directory,
    IOptions<RabbitMqOptions> options,
    TimeProvider time,
    ILogger<OutboxRelayService> logger) : BackgroundService
{
    private const string Group = "relay";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ClaimAfter = TimeSpan.FromMinutes(1);

    private readonly string _consumer = $"relay-{Guid.NewGuid():N}";
    private readonly Dictionary<string, IOutboxSource> _sources = sources.ToDictionary(s => s.Schema, StringComparer.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_sources.Count == 0)
            return;   // this host publishes nothing

        await Task.WhenAll(ReadSignalsAsync(stoppingToken), SweepAsync(stoppingToken));
    }

    private async Task ReadSignalsAsync(CancellationToken stoppingToken)
    {
        var db = redis.GetDatabase();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnsureGroupAsync(db);
                var entries = await db.StreamReadGroupAsync(RedisOutboxSignal.Stream, Group, _consumer, ">", count: 200);
                // Signals another instance read but never acknowledged (it died) are taken over after a minute.
                var claimed = await db.StreamAutoClaimAsync(RedisOutboxSignal.Stream, Group, _consumer, (long)ClaimAfter.TotalMilliseconds, "0-0", count: 200);
                var all = entries.Concat(claimed.ClaimedEntries).Where(e => !e.IsNull).ToArray();
                if (all.Length == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                    continue;
                }

                var work = all
                    .Select(e => (Tenant: Guid.TryParse((string?)e["tenant"], out var id) ? id : Guid.Empty, Schema: (string?)e["schema"] ?? ""))
                    .Where(w => w.Tenant != Guid.Empty && _sources.ContainsKey(w.Schema))
                    .Distinct()
                    .ToList();
                await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = options.Value.RelayParallelism, CancellationToken = stoppingToken }, async (item, ct) =>
                {
                    var tenant = await directory.FindByIdAsync(item.Tenant, ct);
                    if (tenant is { Status: not (TenantStatus.Deleted or TenantStatus.Provisioning) })
                        await RelayAsync(_sources[item.Schema], tenant, ct);
                });
                await db.StreamAcknowledgeAsync(RedisOutboxSignal.Stream, Group, all.Select(e => e.Id).ToArray());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRelayLoopFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task SweepAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.OutboxSweepMinutes), time);
        await SweepOnceAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await SweepOnceAsync(stoppingToken);
    }

    /// <summary>Visits every tenant once: publishes anything left behind. Exposed for tests.</summary>
    internal async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var tenants = (await directory.ListAsync(cancellationToken)).Where(t => t.Status is not (TenantStatus.Deleted or TenantStatus.Provisioning)).ToList();
            await Parallel.ForEachAsync(tenants, new ParallelOptions { MaxDegreeOfParallelism = options.Value.RelayParallelism, CancellationToken = cancellationToken }, async (tenant, ct) =>
            {
                foreach (var source in _sources.Values)
                    await RelayAsync(source, tenant, ct);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSweepFailed(logger, ex);
        }
    }

    private async Task RelayAsync(IOutboxSource source, TenantInfo tenant, CancellationToken cancellationToken)
    {
        try
        {
            while (await source.RelayAsync(tenant, cancellationToken) >= 100)
            {
                // A full batch: there may be more.
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The rows stay in the outbox; the next signal or sweep tries again.
            LogRelayFailed(logger, ex, tenant.Slug, source.Schema);
        }
    }

    private static async Task EnsureGroupAsync(IDatabase db)
    {
        try
        {
            await db.StreamCreateConsumerGroupAsync(RedisOutboxSignal.Stream, Group, "0", createStream: true);
        }
        catch (RedisServerException ex) when (ex.Message.StartsWith("BUSYGROUP", StringComparison.Ordinal))
        {
            // already exists
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox relay for {Tenant}/{Schema} failed; it will be retried")]
    private static partial void LogRelayFailed(ILogger logger, Exception exception, string tenant, string schema);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox signal loop failed; retrying")]
    private static partial void LogRelayLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox sweep failed; the next sweep will retry")]
    private static partial void LogSweepFailed(ILogger logger, Exception exception);
}
