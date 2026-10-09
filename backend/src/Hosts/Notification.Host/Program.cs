using Acme.Notification;
using Acme.Core.Hosting;
using Acme.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Notification)
    .AddModule<NotificationModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
