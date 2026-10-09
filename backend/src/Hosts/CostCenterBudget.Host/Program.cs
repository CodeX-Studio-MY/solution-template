using Acme.CostCenterBudget;
using Acme.Core.Hosting;
using Acme.Core.Platform;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatform(PlatformServices.CostCenterBudget)
    .AddModule<CostCenterBudgetModule>();

var app = builder.Build();
app.UsePlatform();
app.Run();
