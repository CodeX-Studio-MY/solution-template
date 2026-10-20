using Farm360.Core.Data;
using Farm360.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Farm360.Core.Messaging;

public static class MessagingRegistration
{
    /// <summary>Registers a module's outbox, so the relay publishes the events it saves. Call once per module that publishes events.</summary>
    public static IServiceCollection AddModuleOutbox<TContext>(this IServiceCollection services, string schema)
        where TContext : ModuleDbContext
    {
        services.AddSingleton<IOutboxSource>(sp => new OutboxSource<TContext>(
            schema,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IMessageBroker>(),
            sp.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Registers a consumer. <paramref name="ownerModule"/> is the module the consumer belongs to: it only receives events for
    /// tenants entitled to that module (null for platform consumers, which serve every tenant). Its inbox lives in <typeparamref name="TContext"/>.
    /// </summary>
    public static IServiceCollection AddEventConsumer<TEvent, TConsumer, TContext>(this IServiceCollection services, PlatformService service, string? ownerModule)
        where TEvent : class
        where TConsumer : class, IEventConsumer<TEvent>
        where TContext : ModuleDbContext
    {
        services.TryAddScoped<TConsumer>();
        services.AddSingleton(new EventConsumerRegistration(EventRegistry.For<TEvent>(), typeof(TConsumer), typeof(TContext), ownerModule, service.Key));
        return services;
    }
}
