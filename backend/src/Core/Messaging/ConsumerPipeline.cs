using System.Diagnostics;
using System.Text.Json;
using Farm360.Core.Data;
using Farm360.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Farm360.Core.Messaging;

/// <summary>One consumer: which event, which handler, which module owns it, and which context holds its inbox.</summary>
public sealed record EventConsumerRegistration(
    EventDescriptor Event,
    Type ConsumerType,
    Type ContextType,
    string? OwnerModule,
    string ServiceKey)
{
    /// <summary>The consumer's identity in the inbox and the queue name suffix: stable across restarts, and distinct per consumer type, so two consumers of one event in a service never share a queue.</summary>
    public string Name => $"{ServiceKey}.{ConsumerType.Name}.{Event.RoutingKey}";
}

public enum ConsumeOutcome
{
    /// <summary>Handled now, or already handled before (a duplicate delivery).</summary>
    Done,

    /// <summary>Not for this tenant: unknown, deleted, or not entitled to the owning module. Acknowledge and drop.</summary>
    Skipped,

    /// <summary>Try again later (the tenant is read-only for now).</summary>
    Retry,
}

/// <summary>
/// Handles a delivered envelope: restores the tenant, checks it is entitled to the consumer's module (an event reaches a
/// module's consumers only for tenants that have that module), then runs the handler and records the inbox row in one
/// transaction, so a duplicate delivery is recognised and the handler's writes happen once.
/// </summary>
public sealed partial class ConsumerPipeline(IServiceScopeFactory scopes, ITenantDirectory directory, TimeProvider time, ILogger<ConsumerPipeline> logger)
{
    private static readonly System.Diagnostics.ActivitySource Source = new("Farm360.Core.Messaging");

    public async Task<ConsumeOutcome> ProcessAsync(EventConsumerRegistration registration, EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var tenant = await directory.FindByIdAsync(envelope.TenantId, cancellationToken);
        if (tenant is null || tenant.Status is TenantStatus.Deleted or TenantStatus.Provisioning)
            return ConsumeOutcome.Skipped;
        // A module's consumers only get events for tenants entitled to it. Platform consumers (Notification) have no module: every tenant.
        if (registration.OwnerModule is { } owner && !tenant.EnabledModules.Contains(owner))
        {
            LogNotEntitled(logger, registration.Name, tenant.Slug);
            return ConsumeOutcome.Skipped;
        }
        if (!tenant.CanWrite)
            return tenant.Status == TenantStatus.PendingDeletion ? ConsumeOutcome.Skipped : ConsumeOutcome.Retry;

        using var activity = StartActivity(registration, envelope, tenant);
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextSetter>().SetTenant(tenant);
        var db = (ModuleDbContext)scope.ServiceProvider.GetRequiredService(registration.ContextType);

        var payload = JsonSerializer.Deserialize(envelope.Payload, registration.Event.Type, EventJson.Options)
            ?? throw new JsonException($"The payload of {envelope.RoutingKey} is empty.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.InboxMessages.AnyAsync(i => i.EventId == envelope.EventId && i.Consumer == registration.Name, cancellationToken))
        {
            LogDuplicate(logger, registration.Name, envelope.EventId);
            return ConsumeOutcome.Done;
        }

        var consumer = scope.ServiceProvider.GetRequiredService(registration.ConsumerType);
        var handle = registration.ConsumerType.GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEventConsumer<>) && i.GenericTypeArguments[0] == registration.Event.Type)
            .GetMethod(nameof(IEventConsumer<object>.HandleAsync))!;
        try
        {
            await (Task)handle.Invoke(consumer, [payload, cancellationToken])!;
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is not null)
        {
            // Reflection wraps what the handler threw; the dead-letter queue should show the real error.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }

        db.InboxMessages.Add(InboxMessage.Create(envelope.EventId, registration.Name, time.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ConsumeOutcome.Done;
    }

    private static Activity? StartActivity(EventConsumerRegistration registration, EventEnvelope envelope, TenantInfo tenant)
    {
        // The trace continues from the producer's span through the queue.
        ActivityContext.TryParse(envelope.TraceParent, null, out var parent);
        var activity = Source.StartActivity($"consume {envelope.RoutingKey}", ActivityKind.Consumer, parent);
        activity?.SetTag(TelemetryTags.TenantId, tenant.Id.ToString());
        activity?.SetTag("messaging.consumer", registration.Name);
        return activity;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Consumer}: tenant {Tenant} is not entitled to the module; event dropped")]
    private static partial void LogNotEntitled(ILogger logger, string consumer, string tenant);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Consumer}: duplicate delivery of {EventId} ignored")]
    private static partial void LogDuplicate(ILogger logger, string consumer, Guid eventId);
}
