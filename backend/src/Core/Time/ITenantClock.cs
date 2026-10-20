using Farm360.Core.Tenancy;

namespace Farm360.Core.Time;

/// <summary>
/// The current date and time in the tenant's time zone (IANA ID). Business dates (order date, harvest date)
/// come from <see cref="Today"/>; stored timestamps are always UTC from <see cref="TimeProvider"/>.
/// </summary>
public interface ITenantClock
{
    DateOnly Today { get; }

    DateTimeOffset Now { get; }

    TimeZoneInfo TimeZone { get; }
}

internal sealed class TenantClock(TimeProvider time, ITenantContext tenant) : ITenantClock
{
    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(tenant.Tenant.TimeZone);

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
}
