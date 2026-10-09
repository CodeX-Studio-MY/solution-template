using Acme.Notification.Infrastructure;
using Acme.Core.Data;
using Acme.Core.Composition;
using Acme.Core.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.Notification;

/// <summary>Real-time notifications and email.</summary>
public sealed class NotificationModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Notification;

    public static void Register(IHostApplicationBuilder builder)
    {
        builder.Services.AddModuleDbContext<NotificationDbContext>(Service.Schema);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
