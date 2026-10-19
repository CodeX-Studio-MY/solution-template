namespace Saaskit.Contracts;

/// <summary>
/// Events the platform itself produces or consumes, for every module to use. One type per version; change by adding optional
/// fields only. A module's own events live next to these, in a file named after the module.
/// </summary>
public static class TenantDayStarted
{
    /// <summary>
    /// The Scheduler's daily trigger for one tenant: it is 09:00 there. A module that has daily work (reminders, digests, expiry)
    /// consumes it and does that work in the tenant's own scope. One message per tenant, handled in parallel; a misfire or a second
    /// firing produces the same event ID, so the consumer's inbox handles it once.
    /// </summary>
    [IntegrationEvent("scheduler.tenant-day-started", 1)]
    public sealed record V1(DateOnly BusinessDate);
}

public static class NotificationRequested
{
    /// <summary>
    /// Asks the Notification service to tell people something, in the app (history and real-time push) and by email. The producer
    /// localizes the text in the tenant's language, because it owns the message catalog for its own words. Address it to one
    /// person (<paramref name="RecipientId"/>), or to everyone holding at least <paramref name="RecipientMinimumRole"/> in
    /// <paramref name="RecipientModule"/>.
    /// </summary>
    /// <param name="Kind">A stable key for the kind of notification, <c>{module-key}.{what}</c>, for filtering and analytics.</param>
    /// <param name="Title">The short title, already localized.</param>
    /// <param name="Body">The text, already localized. It is also the email body.</param>
    /// <param name="Link">A path in the web app the notification opens, or null.</param>
    /// <param name="RecipientId">One principal to tell.</param>
    /// <param name="RecipientModule">Tell the members of this module instead.</param>
    /// <param name="RecipientMinimumRole">With <paramref name="RecipientModule"/>: the lowest role that is told (default <c>user</c>).</param>
    /// <param name="EmailSubject">The email subject; the title when null.</param>
    [IntegrationEvent("platform.notification-requested", 1)]
    public sealed record V1(
        string Kind,
        string Title,
        string Body,
        string? Link,
        Guid? RecipientId = null,
        string? RecipientModule = null,
        string? RecipientMinimumRole = null,
        string? EmailSubject = null);
}
