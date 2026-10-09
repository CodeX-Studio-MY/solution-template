using Acme.Identity;
using Acme.Core.Hosting;
using Acme.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Identity)
    .AddModule<IdentityModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
