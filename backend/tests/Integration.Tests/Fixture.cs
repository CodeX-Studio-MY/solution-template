using System.ComponentModel.DataAnnotations;
using System.Text;
using Saaskit.Contracts;
using Saaskit.Core.Hosting.Messaging;
using Saaskit.Core.Messaging;
using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;
using Saaskit.Notification.Application;
using Saaskit.Notification.Infrastructure;
using RabbitMQ.Client;

// The fixture's settings and flag below join the ones declared in Contracts, for this test assembly only.
[assembly: DeclaresCustomization]

namespace Saaskit.Integration.Tests;

/// <summary>
/// The test-only fixture the platform tests use in place of a business module. The template ships no business module, so the
/// tests borrow the keys of platform modules: <see cref="Entitled"/> is in every plan, and <see cref="Optional"/> is the module a test
/// takes away from a tenant to see entitlement at work. Consumers run inside the Notification service, which every product has.
/// </summary>
internal static class Fixture
{
    /// <summary>A module every tenant has, to hold roles in.</summary>
    public const string Entitled = Modules.AccessManagement;

    /// <summary>A module a test removes from a tenant (<see cref="TestPlatformHost"/>'s <c>demo</c> doesn't have it).</summary>
    public const string Optional = Modules.Billing;

    /// <summary>The service the fixture's endpoints and consumers are hosted as.</summary>
    public static PlatformService Service => PlatformServices.Notification;

    /// <summary>The first plan in platform.yaml. Tests never name a plan: a product chooses its own.</summary>
    public static string BasePlan => Plans.All[0];

    /// <summary>The last plan in platform.yaml (the same as <see cref="BasePlan"/> when there is one).</summary>
    public static string TopPlan => Plans.All[^1];
}

/// <summary>Per-tenant settings in the shape a business module declares them: tenant-editable values and operator-only choices.</summary>
[TenantSettings(Fixture.Entitled)]
public sealed record FixtureSettings
{
    /// <summary>A threshold the tenant's admin may set.</summary>
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal AutoApproveBelow { get; init; } = 500m;

    /// <summary>A strategy only operators choose.</summary>
    [AllowedValues("single", "two-level")]
    public string ApprovalPolicy { get; init; } = "single";

    /// <summary>Another tenant-editable value, left at its default by the tests.</summary>
    [TenantEditable, Range(0, 1_000_000_000)]
    public decimal SecondApprovalAbove { get; init; } = 5000m;
}

/// <summary>A feature flag in the shape a business module declares it.</summary>
public static class FixtureFeatures
{
    /// <summary>Off by default.</summary>
    [FeatureFlag(Fixture.Entitled, DefaultEnabled = false, Description = "Share a draft with a link.")]
    public const string DraftSharing = "fixture.draft-sharing";
}

/// <summary>
/// A module's daily work, as a test sees it: on <see cref="TenantDayStarted.V1"/> it publishes one <see cref="NotificationRequested.V1"/>
/// through the outbox of the tenant's database. So the inbox, the outbox, entitlement and tenant scope can all be observed.
/// </summary>
internal sealed class DayEchoConsumer(NotificationDbContext db, IEventPublisher publisher, ITenantContext tenant) : IEventConsumer<TenantDayStarted.V1>
{
    public async Task HandleAsync(TenantDayStarted.V1 integrationEvent, CancellationToken cancellationToken)
    {
        publisher.Publish(db, new NotificationRequested.V1("fixture.day", $"{tenant.Tenant.Slug} {integrationEvent.BusinessDate:yyyy-MM-dd}", "", null));
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Nobody to tell: lets the Notification service consume requests without an Identity service to ask.</summary>
internal sealed class NoContacts : IContactDirectory
{
    public Task<Contact?> FindAsync(Guid principalId, CancellationToken cancellationToken = default) => Task.FromResult<Contact?>(null);

    public Task<IReadOnlyList<Contact>> MembersOfAsync(string moduleKey, string minimumRole, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Contact>>([]);
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
        public static readonly string EventExchange = $"{PlatformInfo.Product}.events";
    }
}

internal sealed record ProbeMessage(IReadOnlyDictionary<string, string> Headers, string Body, string? MessageId);
