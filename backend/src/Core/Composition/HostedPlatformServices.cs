using Farm360.Core.Platform;

namespace Farm360.Core.Composition;

/// <summary>The services this host runs (one, or all of them in <c>AllInOne.Host</c>).</summary>
public sealed class HostedPlatformServices
{
    public List<PlatformService> Services { get; } = [];

    public IReadOnlyList<PlatformService> All => Services;
}
