using Saaskit.Core.Hosting;
using Saaskit.Core.Platform;
using Saaskit.Base;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Base)
    .AddModule<BaseModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
