using Farm360.DevSeed;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Development only. Onboards the demo tenant, then exits. Run by scripts/dev-up through the AppHost.
var builder = WebApplication.CreateBuilder(args);
builder.Configuration["urls"] = "http://127.0.0.1:0";   // it serves nothing
builder.AddEveryModule();
// No consumers, no outbox relay: the real services do that. This process only writes the seed data.
builder.Services.RemoveAll<IHostedService>();

await using var app = builder.Build();
await app.StartAsync();
var output = builder.Configuration["DevSeed:InvitationsFile"] ?? Path.Combine(Directory.GetCurrentDirectory(), ".dev", "invitations.json");
try
{
    await new DemoData(app.Services).SeedAsync(output, app.Lifetime.ApplicationStopping);
}
finally
{
    await app.StopAsync();
}

// scripts/dev-up waits for this file: the seed is finished (also on a run that found nothing left to do).
await File.WriteAllTextAsync(output + ".done", TimeProvider.System.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
