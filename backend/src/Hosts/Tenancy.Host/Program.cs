using Saaskit.Tenancy;
using Saaskit.Core.Hosting;
using Saaskit.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Tenancy)
    .AddModule<TenancyModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
