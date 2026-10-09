using Acme.AppHost;
using Acme.Core.Platform;

var builder = DistributedApplication.CreateBuilder(args);

// deploy/docker-compose.yml is produced from this AppHost with `aspire publish` (scripts/generate), not by hand.
// Only the Gateway and dev UIs (the dashboard, Mailpit) are published to the host.
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard.WithHostPort(PlatformInfrastructure.DashboardHostPort));

// Local dev choices are configuration: Database:Provider (sqlserver or postgresql) and Tenancy:BaseDomain.
var infrastructure = PlatformInfrastructure.Add(builder);

// AppHost:Layout picks how the services run: one host per service (the default, as in production) or all in one process.
IReadOnlyList<IResourceBuilder<ProjectResource>> services;
IResourceBuilder<ProjectResource> tokenServer;
var allInOne = builder.Configuration["AppHost:Layout"] == "all-in-one";
if (allInOne)
{
    tokenServer = infrastructure.AddAllInOne(builder);
    services = [tokenServer];
}
else
{
    services = PlatformResources.AddServices(builder, infrastructure);
    tokenServer = services.Single(s => s.Resource.Name == PlatformServices.Identity.Key);
}
infrastructure.AddScheduler(builder);
if (builder.Configuration["AppHost:Seed"] == "true")
    infrastructure.AddDevSeed(builder, tokenServer);
var gateway = infrastructure.AddGateway(builder, services, allInOne);
PlatformInfrastructure.WireTokenServer(services, tokenServer, gateway);

builder.Build().Run();
