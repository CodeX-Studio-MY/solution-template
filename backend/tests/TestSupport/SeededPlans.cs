using Saaskit.Core.Platform;

namespace Saaskit.TestSupport;

/// <summary>The plans the catalog is seeded with from platform.yaml: the first plan has ID 1, the next 2, and so on.</summary>
public static class SeededPlans
{
    public static short IdOf(string planKey)
    {
        for (var i = 0; i < Plans.All.Count; i++)
            if (Plans.All[i] == planKey)
                return (short)(i + 1);
        throw new ArgumentException($"'{planKey}' is not a plan in platform.yaml.", nameof(planKey));
    }
}
