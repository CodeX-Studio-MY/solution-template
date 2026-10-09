using Acme.Contracts;
using Acme.Core.Composition;
using Acme.Core.Configuration;
using Acme.Core.Data;
using Acme.Core.Localization;
using Acme.Core.Messaging;
using Acme.Core.Platform;
using Acme.Core.ServiceCalls;
using Acme.Notification.Api;
using Acme.Notification.Application;
using Acme.Notification.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Acme.Notification;

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
        services.AddEventConsumer<PurchaseRequestApproved.V1, PurchaseRequestApprovedConsumer, NotificationDbContext>(Service, ownerModule: null);
        services.AddEventConsumer<PurchaseRequestRejected.V1, PurchaseRequestRejectedConsumer, NotificationDbContext>(Service, ownerModule: null);
        services.AddEventConsumer<ApprovalsWaiting.V1, ApprovalsWaitingConsumer, NotificationDbContext>(Service, ownerModule: null);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => NotificationEndpoints.Map(api);
}
