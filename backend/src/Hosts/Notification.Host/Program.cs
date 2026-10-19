using Saaskit.Notification;
using Saaskit.Core.Hosting;
using Saaskit.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Notification)
    .AddModule<NotificationModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
