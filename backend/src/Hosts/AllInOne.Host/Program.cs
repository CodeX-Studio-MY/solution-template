using Saaskit.AllInOne;
using Saaskit.Core.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddEveryModule();

var app = builder.Build();
app.UsePlatform();
app.Run();
