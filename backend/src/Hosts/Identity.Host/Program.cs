using Saaskit.Identity;
using Saaskit.Core.Hosting;
using Saaskit.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Identity)
    .AddModule<IdentityModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
