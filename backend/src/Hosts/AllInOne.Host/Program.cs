using Farm360.AllInOne;
using Farm360.Core.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddEveryModule();

var app = builder.Build();
app.UsePlatform();
app.Run();
