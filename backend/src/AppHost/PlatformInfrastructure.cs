using Acme.Core.Platform;

namespace Acme.AppHost;

/// <summary>
/// The shared infrastructure for local development: the selected database, Valkey, RabbitMQ, object storage
/// emulator, Mailpit and the Migrator. Only the Gateway and dev UIs are published to the host.
/// </summary>
internal sealed class PlatformInfrastructure
{
    /// <summary>The tenant server pool new tenants are placed on.</summary>
    public const string DefaultServerPool = "default";

    public const int DashboardHostPort = 18888;
    public const int MailpitUiHostPort = 8025;

    private PlatformInfrastructure(
        string provider,
        string baseDomain,
        IResourceBuilder<IResourceWithConnectionString> catalog,
        IResourceBuilder<IResourceWithConnectionString> tenantServer,
        IResourceBuilder<IResourceWithConnectionString> redis,
        IResourceBuilder<IResourceWithConnectionString> rabbitMq,
        IResourceBuilder<ProjectResource> migrator)
    {
        Provider = provider;
        BaseDomain = baseDomain;
        Catalog = catalog;
        TenantServer = tenantServer;
        Redis = redis;
        RabbitMq = rabbitMq;
        Migrator = migrator;
    }

    public string Provider { get; }

    public string BaseDomain { get; }

    public IResourceBuilder<IResourceWithConnectionString> Catalog { get; }

    public IResourceBuilder<IResourceWithConnectionString> TenantServer { get; }

    public IResourceBuilder<IResourceWithConnectionString> Redis { get; }

    public IResourceBuilder<IResourceWithConnectionString> RabbitMq { get; }

    public IResourceBuilder<ProjectResource> Migrator { get; }

    public static PlatformInfrastructure Add(IDistributedApplicationBuilder builder)
    {
        var provider = builder.Configuration["Database:Provider"]
            ?? throw new InvalidOperationException("Set Database:Provider (sqlserver or postgresql) in AppHost configuration.");
        var baseDomain = builder.Configuration["Tenancy:BaseDomain"]
            ?? throw new InvalidOperationException("Set Tenancy:BaseDomain in AppHost configuration.");

        IResourceBuilder<IResourceWithConnectionString> server;
        IResourceBuilder<IResourceWithConnectionString> catalog;
        switch (provider)
        {
            case "sqlserver":
                // SQL Server 2025. On Apple Silicon it runs under Docker Desktop's Rosetta emulation (Azure SQL Edge is retired).
                var sql = builder.AddSqlServer("sql").WithImageTag("2025-latest").WithDataVolume(Volume("sql-data"));
                server = sql;
                catalog = sql.AddDatabase("catalog");
                break;
            case "postgresql":
                var postgres = builder.AddPostgres("postgres").WithImageTag("18").WithDataVolume(Volume("postgres-data"));
                server = postgres;
                catalog = postgres.AddDatabase("catalog");
                break;
            default:
                throw new InvalidOperationException($"Unknown Database:Provider '{provider}'.");
        }

        var redis = builder.AddValkey("valkey").WithDataVolume(Volume("valkey-data"));
        var rabbitMq = builder.AddRabbitMQ("rabbitmq").WithManagementPlugin();

        // Object storage emulator for the cloud in platform.yaml: Azurite for azure, SeaweedFS (S3) for aws.
        string cloud = PlatformInfo.Cloud;
        switch (cloud)
        {
            case "azure" when builder.ExecutionContext.IsPublishMode:
                // In docker compose, Azurite runs as a plain container (an Azure resource would publish as Bicep).
                builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite", "3.37.0")
                    .WithVolume(Volume("azurite-data"), "/data")
                    .WithEndpoint(targetPort: 10000, name: "blob", scheme: "http");
                break;
            case "azure":
                builder.AddAzureStorage("storage").RunAsEmulator(azurite => azurite.WithDataVolume(Volume("azurite-data"))).AddBlobs("blobs");
                break;
            case "aws":
                builder.AddSeaweedFS("seaweedfs");
                break;
            default:
                throw new InvalidOperationException($"Unknown cloud '{cloud}'.");
        }

        builder.AddMailPit("mailpit")
            .PublishAsDockerComposeService((_, service) => service.Ports.Add($"{MailpitUiHostPort}:8025"));

        var migrator = builder.AddProject("migrator", "../Migrator/Migrator.csproj")
            .WithArgs("--catalog", "--all");

        var infrastructure = new PlatformInfrastructure(provider, baseDomain, catalog, server, redis, rabbitMq, migrator);
        infrastructure.ConfigureDatabase(migrator).WaitFor(catalog);
        return infrastructure;
    }

    /// <summary>Stable volume names, so the published compose file is the same on every machine.</summary>
    private static string Volume(string name) => $"{PlatformInfo.Product}-{name}";

    /// <summary>Gives a service host its configuration and starts it after the Migrator has finished.</summary>
    public IResourceBuilder<ProjectResource> ConfigureService(IResourceBuilder<ProjectResource> service) =>
        ConfigureDatabase(service)
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain)
            .WithEnvironment(ConfigKeys.Redis.ConnectionString, Redis)
            .WithHttpHealthCheck("/health/ready")
            .WaitFor(Redis)
            .WaitForCompletion(Migrator);

    private IResourceBuilder<ProjectResource> ConfigureDatabase(IResourceBuilder<ProjectResource> project) =>
        project
            .WithEnvironment(ConfigKeys.Database.Provider, Provider)
            .WithEnvironment(ConfigKeys.Database.CatalogConnectionString, Catalog)
            .WithEnvironment(ConfigKeys.Database.Servers(DefaultServerPool), TenantServer)
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain);
}
