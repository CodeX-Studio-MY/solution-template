using Saaskit.Core.Time;

namespace Saaskit.Core.Tests;

public sealed class TenantScheduleTests
{
    private static readonly TimeOnly NineAm = new(9, 0);

    [Theory]
    [InlineData("Asia/Kuala_Lumpur", "2026-10-09T01:00:00Z", true)]    // 09:00 local
    [InlineData("Asia/Kuala_Lumpur", "2026-10-09T01:14:59Z", true)]    // the last second of the window
    [InlineData("Asia/Kuala_Lumpur", "2026-10-09T01:15:00Z", false)]   // the next tick is outside it
    [InlineData("Asia/Kuala_Lumpur", "2026-10-09T00:45:00Z", false)]   // 08:45 local
    [InlineData("Asia/Kolkata", "2026-10-09T03:30:00Z", true)]         // 09:00 at UTC+5:30
    [InlineData("Asia/Kathmandu", "2026-10-09T03:15:00Z", true)]       // 09:00 at UTC+5:45
    [InlineData("America/New_York", "2026-10-09T13:00:00Z", true)]     // 09:00 EDT
    [InlineData("America/New_York", "2026-11-09T14:00:00Z", true)]     // 09:00 EST: daylight saving moved the UTC time
    [InlineData("America/New_York", "2026-11-09T13:00:00Z", false)]
    public void The_window_is_the_quarter_hour_from_the_local_target_time(string zone, string utc, bool expected) =>
        TenantSchedule.IsDue(zone, DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), NineAm, out _).ShouldBe(expected);

    [Fact]
    public void Exactly_one_tick_a_day_is_due_for_every_zone()
    {
        foreach (var zone in new[] { "Asia/Kuala_Lumpur", "Asia/Kolkata", "Asia/Kathmandu", "America/New_York", "Pacific/Chatham", "Pacific/Kiritimati" })
        {
            var start = new DateTimeOffset(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
            var due = Enumerable.Range(0, 96).Count(i => TenantSchedule.IsDue(zone, start.AddMinutes(15 * i), NineAm, out _));
            due.ShouldBe(1, zone);
        }
    }

    [Fact]
    public void The_local_date_is_the_tenants_not_UTCs()
    {
        // 23:00 UTC on 8 October is 07:00 on 9 October in Kuala Lumpur; 09:00 there is 01:00 UTC.
        TenantSchedule.IsDue("Pacific/Kiritimati", new DateTimeOffset(2026, 10, 8, 19, 0, 0, TimeSpan.Zero), NineAm, out var date).ShouldBeTrue();   // UTC+14 → 09:00 on the 9th
        date.ShouldBe(new DateOnly(2026, 10, 9));
    }

    [Fact]
    public void The_event_ID_is_stable_per_job_tenant_and_day_and_differs_otherwise()
    {
        var tenant = Guid.NewGuid();
        var day = new DateOnly(2026, 10, 9);
        TenantSchedule.EventId("job", tenant, day).ShouldBe(TenantSchedule.EventId("job", tenant, day));
        TenantSchedule.EventId("job", tenant, day).ShouldNotBe(TenantSchedule.EventId("job", tenant, day.AddDays(1)));
        TenantSchedule.EventId("job", tenant, day).ShouldNotBe(TenantSchedule.EventId("job", Guid.NewGuid(), day));
        TenantSchedule.EventId("job", tenant, day).ShouldNotBe(TenantSchedule.EventId("other", tenant, day));
    }
}
