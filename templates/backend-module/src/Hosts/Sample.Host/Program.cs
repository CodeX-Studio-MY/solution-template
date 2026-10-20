using Farm360.Core.Hosting;
using Farm360.Core.Platform;
using Farm360.Sample;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Sample)
    .AddModule<SampleModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
