using Farm360.Identity;
using Farm360.Core.Hosting;
using Farm360.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Identity)
    .AddModule<IdentityModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
