using Farm360.Notification;
using Farm360.Core.Hosting;
using Farm360.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Notification)
    .AddModule<NotificationModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
