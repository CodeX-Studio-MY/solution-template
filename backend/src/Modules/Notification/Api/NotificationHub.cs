using Saaskit.Core.Security;
using Saaskit.Notification.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Saaskit.Notification.Api;

/// <summary>
/// Real-time notifications. Connections are authenticated (the Gateway attaches the access token to the WebSocket upgrade, so the
/// browser never sees it) and join two groups: the tenant's, and the person's own. The hub has no methods the client can call.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>The client-side event name the browser listens for.</summary>
    public const string PushMethod = "notify";

    public static string TenantGroup(Guid tenantId) => $"tenant:{tenantId}";

    public static string UserGroup(Guid tenantId, Guid userId) => $"user:{tenantId}:{userId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;
        if (user.GetTenantId() is { } tenantId && Guid.TryParse(user.FindFirst(PlatformClaims.Subject)?.Value, out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId));
        }
        await base.OnConnectedAsync();
    }
}

internal sealed class SignalRNotificationPusher(IHubContext<NotificationHub> hub) : INotificationPusher
{
    public Task PushAsync(Guid tenantId, Guid recipientId, NotificationDto notification, CancellationToken cancellationToken = default) =>
        hub.Clients.Group(NotificationHub.UserGroup(tenantId, recipientId)).SendAsync(NotificationHub.PushMethod, notification, cancellationToken);
}
