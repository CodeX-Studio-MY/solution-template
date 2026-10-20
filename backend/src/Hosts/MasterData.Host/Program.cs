using Farm360.Core.Hosting;
using Farm360.Core.Platform;
using Farm360.MasterData;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.MasterData)
    .AddModule<MasterDataModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
