namespace Saaskit.TestSupport;

/// <summary>
/// Tests whose hosts publish or consume events share one RabbitMQ, and a consumer queue is shared by every host that declares it.
/// Run them one class at a time, so one test's host never receives another test's events.
/// </summary>
public static class BrokerGroup
{
    public const string Name = "broker";
}
