using Farm360.Tenancy;
using Farm360.Core.Hosting;
using Farm360.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Tenancy)
    .AddModule<TenancyModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
