using Farm360.Core.Http;
using Farm360.Notification.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Farm360.Notification.Api;

/// <summary>A person's own notifications. Available to every signed-in principal; there is no module role to hold.</summary>
internal static class NotificationEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/v1/notifications").WithTags("notifications");
        group.MapGet("", (NotificationService service, [AsParameters] PageRequest page, bool? unread, CancellationToken ct) =>
            service.ListAsync(page, unread ?? false, ct));
        group.MapGet("/unread-count", async (NotificationService service, CancellationToken ct) => new UnreadCount(await service.UnreadCountAsync(ct)));
        group.MapPost("/{id:guid}/read", async (Guid id, NotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        });
        group.MapPost("/read-all", async (NotificationService service, CancellationToken ct) => new UnreadCount(await service.MarkAllReadAsync(ct)));

        // The SignalR hub: /api/notification/hub (through the Gateway, like every other API).
        api.MapHub<NotificationHub>("/hub");
    }

    internal sealed record UnreadCount(int Count);
}
