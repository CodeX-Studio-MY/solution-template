using Farm360.Contracts;
using Farm360.Core.Composition;
using Farm360.Core.Configuration;
using Farm360.Core.Data;
using Farm360.Core.Localization;
using Farm360.Core.Messaging;
using Farm360.Core.Platform;
using Farm360.Core.ServiceCalls;
using Farm360.Notification.Api;
using Farm360.Notification.Application;
using Farm360.Notification.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Farm360.Notification;

/// <summary>Real-time notifications (SignalR with a Redis backplane) and email. Available to every signed-in principal and every tenant.</summary>
public sealed class NotificationModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Notification;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<NotificationDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(NotificationModule).Assembly);
        services.AddPlatformOptions<SmtpOptions>(builder.Configuration);

        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<NotificationService>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddScoped<INotificationPusher, SignalRNotificationPusher>();
        services.AddPlatformServiceClient<IContactDirectory, IdentityDirectoryClient>(PlatformServices.Identity);

        // Every instance sees every push: SignalR groups span instances through Redis.
        services.AddSignalR().AddStackExchangeRedis(
            builder.Configuration["Redis:ConnectionString"] ?? throw new InvalidOperationException("Redis:ConnectionString is required."),
            options => options.Configuration.ChannelPrefix = RedisChannel.Literal($"{PlatformInfo.Product}:signalr"));

        // Platform consumers have no module: they serve every tenant.
        services.AddEventConsumer<NotificationRequested.V1, NotificationRequestedConsumer, NotificationDbContext>(Service, ownerModule: null);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => NotificationEndpoints.Map(api);
}
