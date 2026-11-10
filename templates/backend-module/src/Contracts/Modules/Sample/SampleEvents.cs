namespace Saaskit.Contracts.Sample;

/// <summary>
/// The integration events this module publishes, for other modules to consume. One static class per event, one nested record per
/// version; never change a published <c>V1</c>, add a <c>V2</c> and publish both until every consumer has moved. Name events
/// <c>sample-key.{what-happened}</c>: the module key first (an architecture test checks it). Replace the example with your own.
/// </summary>
public static class ItemCreated
{
    /// <summary>An item was created. Published in the same save as the item, so it is never sent for one that wasn't stored.</summary>
    [IntegrationEvent("sample-key.item-created", 1)]
    public sealed record V1(Guid ItemId, string Name);
}
