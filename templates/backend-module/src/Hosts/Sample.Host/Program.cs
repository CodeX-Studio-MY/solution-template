using Acme.Core.Hosting;
using Acme.Core.Platform;
using Acme.Sample;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Sample)
    .AddModule<SampleModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
