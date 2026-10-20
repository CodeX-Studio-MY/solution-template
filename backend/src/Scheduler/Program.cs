using Farm360.Core.Configuration;
using Farm360.Core.Hosting;
using Farm360.Core.Hosting.Messaging;
using Farm360.Scheduler;

var builder = WebApplication.CreateBuilder(args);

builder.AddPlatformInfrastructure("scheduler");
builder.Services.AddPlatformMessaging(builder.Configuration);   // the Scheduler publishes events
builder.Services.AddPlatformScheduler(builder.Configuration.GetValidatedOptions<DatabaseOptions>());

var app = builder.Build();
app.UseExceptionHandler();
app.MapPlatformHealth();
app.Run();

/// <summary>Entry point marker for the integration tests.</summary>
public partial class Program;
