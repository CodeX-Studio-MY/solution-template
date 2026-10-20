namespace Farm360.Contracts;

/// <summary>
/// Names an integration event and its version. The routing key is <c>{Name}.v{Version}</c>. Events only ever change by adding
/// optional fields; anything breaking becomes a new version (a new nested type, for example <c>V2</c>), and both are published
/// until every consumer has moved.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventAttribute(string name, int version) : Attribute
{
    /// <summary>The event name: <c>{producing-module-key}.{what-happened}</c>.</summary>
    public string Name { get; } = name;

    public int Version { get; } = version;

    public string RoutingKey => $"{Name}.v{Version}";
}
