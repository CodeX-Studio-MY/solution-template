using Saaskit.Core.Hosting;
using Saaskit.Core.Platform;
using Saaskit.Sample;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.Sample)
    .AddModule<SampleModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
