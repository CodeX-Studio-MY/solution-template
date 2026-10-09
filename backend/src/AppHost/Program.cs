using Acme.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

// deploy/docker-compose.yml is produced from this AppHost with `aspire publish` (scripts/generate), not by hand.
// Only the Gateway and dev UIs (the dashboard, Mailpit) are published to the host.
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(dashboard => dashboard.WithHostPort(PlatformInfrastructure.DashboardHostPort));

// Local dev choices are configuration: Database:Provider (sqlserver or postgresql) and Tenancy:BaseDomain.
var infrastructure = PlatformInfrastructure.Add(builder);
var services = PlatformResources.AddServices(builder, infrastructure);
var gateway = infrastructure.AddGateway(builder, services);
PlatformInfrastructure.WireTokenServer(services, gateway);

builder.Build().Run();
