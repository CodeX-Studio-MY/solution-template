using Acme.PurchaseRequest;
using Acme.Core.Hosting;
using Acme.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.PurchaseRequest)
    .AddModule<PurchaseRequestModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
