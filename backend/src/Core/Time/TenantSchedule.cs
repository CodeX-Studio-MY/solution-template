using System.Security.Cryptography;
using System.Text;
using Farm360.Core.Tenancy;

namespace Farm360.Core.Time;

/// <summary>
/// "Run at 09:00 tenant local time" without looping over thousands of tenants serially inside one trigger. A frequent trigger asks
/// each tenant "is your local time inside the window that starts at 09:00?"; exactly one tick in a day says yes.
/// Fired every 15 minutes it also covers zones with 30- and 45-minute offsets (India, Nepal).
/// </summary>
public static class TenantSchedule
{
    /// <summary>The tick spacing. A window of this length starting at the target time is hit by exactly one tick per day.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    /// <summary>Whether <paramref name="utcNow"/> falls in the window [<paramref name="localTime"/>, +<see cref="TickInterval"/>) in the tenant's zone.</summary>
    public static bool IsDue(TenantInfo tenant, DateTimeOffset utcNow, TimeOnly localTime) => IsDue(tenant.TimeZone, utcNow, localTime, out _);

    public static bool IsDue(string timeZoneId, DateTimeOffset utcNow, TimeOnly localTime, out DateOnly localDate)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        localDate = DateOnly.FromDateTime(local.DateTime);
        var sinceTarget = TimeOnly.FromDateTime(local.DateTime).ToTimeSpan() - localTime.ToTimeSpan();
        return sinceTarget >= TimeSpan.Zero && sinceTarget < TickInterval;
    }

    /// <summary>
    /// A stable ID for "this job, this tenant, this day". If a trigger fires twice (a misfire, a restart), both messages carry the same
    /// ID and the consumers' inbox handles it once.
    /// </summary>
    public static Guid EventId(string job, Guid tenantId, DateOnly localDate)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{job}|{tenantId}|{localDate:yyyy-MM-dd}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
