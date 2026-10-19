using Saaskit.Core.Tenancy;
using Saaskit.Core.Time;
using Microsoft.Extensions.Time.Testing;

namespace Saaskit.Core.Tests;

public sealed class TenantClockTests
{
    [Fact]
    public void Today_is_the_tenants_business_date()
    {
        // 16:30 UTC on 1 January is 00:30 on 2 January in Kuala Lumpur (UTC+8).
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 16, 30, 0, TimeSpan.Zero));
        var tenant = new TenantContext();
        tenant.SetTenant(TestTenants.Create());

        var clock = new TenantClock(time, tenant);

        clock.Today.ShouldBe(new DateOnly(2026, 1, 2));
        clock.Now.Offset.ShouldBe(TimeSpan.FromHours(8));
    }

    [Fact]
    public void A_scope_cannot_switch_tenants()
    {
        var tenant = new TenantContext();
        tenant.SetTenant(TestTenants.Create("a"));
        Should.Throw<InvalidOperationException>(() => tenant.SetTenant(TestTenants.Create("b")));
    }
}
