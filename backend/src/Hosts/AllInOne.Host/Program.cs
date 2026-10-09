using Acme.AllInOne;
using Acme.Core.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddEveryModule();

var app = builder.Build();
app.UsePlatform();
app.Run();
