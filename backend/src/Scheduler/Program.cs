using Saaskit.Core.Configuration;
using Saaskit.Core.Hosting;
using Saaskit.Core.Hosting.Messaging;
using Saaskit.Scheduler;

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
