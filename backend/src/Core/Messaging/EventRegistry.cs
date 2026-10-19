using System.Collections.Frozen;
using System.Reflection;
using Saaskit.Contracts;

namespace Saaskit.Core.Messaging;

/// <summary>One integration event type: its name, version and routing key.</summary>
public sealed record EventDescriptor(Type Type, string Name, int Version)
{
    public string RoutingKey => $"{Name}.v{Version}";
}

/// <summary>Every integration event in <c>Contracts</c>, found by its <see cref="IntegrationEventAttribute"/>.</summary>
public static class EventRegistry
{
    private static readonly FrozenDictionary<Type, EventDescriptor> ByType = Discover();
    private static readonly FrozenDictionary<string, EventDescriptor> ByRoutingKey = ByType.Values.ToFrozenDictionary(d => d.RoutingKey, StringComparer.Ordinal);

    public static IReadOnlyCollection<EventDescriptor> All => ByType.Values;

    public static EventDescriptor For<T>() => For(typeof(T));

    public static EventDescriptor For(Type type) =>
        ByType.TryGetValue(type, out var descriptor)
            ? descriptor
            : throw new InvalidOperationException($"{type.FullName} is not an integration event. Declare it in Contracts with [IntegrationEvent].");

    public static EventDescriptor? Find(string routingKey) => ByRoutingKey.GetValueOrDefault(routingKey);

    private static FrozenDictionary<Type, EventDescriptor> Discover() =>
        typeof(IntegrationEventAttribute).Assembly.GetTypes()
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<IntegrationEventAttribute>()))
            .Where(x => x.Attribute is not null)
            .ToFrozenDictionary(x => x.Type, x => new EventDescriptor(x.Type, x.Attribute!.Name, x.Attribute.Version));
}
