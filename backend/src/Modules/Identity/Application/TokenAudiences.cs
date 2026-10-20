using Farm360.Core.Platform;

namespace Farm360.Identity.Application;

/// <summary>Maps the <c>resource</c> parameter (RFC 8707) to the audiences of the platform's services.</summary>
public static class TokenAudiences
{
    /// <summary>The audience of every service in platform.yaml, <c>{product}:{service-key}</c>.</summary>
    public static IReadOnlySet<string> Known { get; } = PlatformServices.All.Select(s => s.Audience).ToHashSet(StringComparer.Ordinal);

    /// <summary>Returns the requested audiences, or null when any of them is not a known service audience.</summary>
    public static IReadOnlyList<string>? Resolve(IEnumerable<string> requested, string? defaultAudience)
    {
        var list = requested.Distinct(StringComparer.Ordinal).ToList();
        if (list.Count == 0 && defaultAudience is not null)
            list.Add(defaultAudience);
        return list.Count > 0 && list.All(Known.Contains) ? list : null;
    }
}
