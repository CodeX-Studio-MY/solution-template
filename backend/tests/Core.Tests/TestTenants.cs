using Saaskit.Core.Platform;
using Saaskit.Core.Tenancy;

namespace Saaskit.Core.Tests;

internal static class TestTenants
{
    public static TenantInfo Create(string slug = "demo", TenantStatus status = TenantStatus.Active, string provider = "fake") => new(
        Guid.NewGuid(),
        slug,
        slug,
        status,
        Plans.All[0],
        "Asia/Kuala_Lumpur",
        "MYR",
        "en",
        new HashSet<string>(StringComparer.Ordinal),
        new TenantPlacement(provider, "default", $"tenant_{slug}", null));
}
