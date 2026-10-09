using Acme.Tenancy;
using Acme.Core.Hosting;
using Acme.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Tenancy)
    .AddModule<TenancyModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
