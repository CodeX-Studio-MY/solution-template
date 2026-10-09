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
    public const int PublicPort = 7443;
    public const int MailpitUiHostPort = 8025;

    /// <summary>The generated, persisted secrets shared by the stack. Values never appear in the repository.</summary>
    private readonly Secrets _secrets;

    private PlatformInfrastructure(
        string provider,
        string baseDomain,
        IResourceBuilder<IResourceWithConnectionString> catalog,
        IResourceBuilder<IResourceWithConnectionString> tenantServer,
        IResourceBuilder<IResourceWithConnectionString> redis,
        IResourceBuilder<IResourceWithConnectionString> rabbitMq,
        IResourceBuilder<ProjectResource> migrator,
        Secrets secrets)
    {
        _secrets = secrets;
        Provider = provider;
        BaseDomain = baseDomain;
        Catalog = catalog;
        TenantServer = tenantServer;
        Redis = redis;
        RabbitMq = rabbitMq;
        Migrator = migrator;
    }

    public static string AppHostDirectory => AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
        : AppContext.BaseDirectory;

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

        var infrastructure = new PlatformInfrastructure(provider, baseDomain, catalog, server, redis, rabbitMq, migrator, Secrets.Add(builder));
        infrastructure.ConfigureDatabase(migrator).WaitFor(catalog);
        return infrastructure;
    }

    /// <summary>Stable volume names, so the published compose file is the same on every machine.</summary>
    private static string Volume(string name) => $"{PlatformInfo.Product}-{name}";

    /// <summary>Gives a service host its configuration and starts it after the Migrator has finished.</summary>
    public IResourceBuilder<ProjectResource> ConfigureService(IResourceBuilder<ProjectResource> service)
    {
        service = ConfigureDatabase(service)
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain)
            .WithEnvironment(ConfigKeys.Tenancy.PublicPort, PublicPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment(ConfigKeys.Redis.ConnectionString, Redis)
            .WithEnvironment(ConfigKeys.DataProtection.KeyEncryptionKey, _secrets.DataProtectionKey)
            .WithEnvironment(ConfigKeys.ServiceClient.ClientSecret, _secrets.ServiceSecrets[service.Resource.Name])
            .WithHttpHealthCheck("/health/ready")
            .WaitFor(Redis)
            .WaitForCompletion(Migrator);

        if (service.Resource.Name == PlatformServices.Identity.Key)
        {
            // Only the token server holds the signing and encryption keys; it also registers every platform client.
            service = service
                // The token server sits on the private network behind the Gateway, which terminates TLS.
                .WithEnvironment(ConfigKeys.TokenServer.RequireTransportSecurity, "false")
                .WithEnvironment(ConfigKeys.TokenServer.SigningKeys(0), _secrets.SigningKey)
                .WithEnvironment(ConfigKeys.TokenServer.EncryptionKeys(0), _secrets.EncryptionKey)
                .WithEnvironment(ConfigKeys.TokenServer.PlatformClientSecrets(PlatformClients.Bff), _secrets.BffSecret);
            foreach (var platformService in PlatformServices.All)
                service = service.WithEnvironment(ConfigKeys.TokenServer.PlatformClientSecrets(platformService.ClientId), _secrets.ServiceSecrets[platformService.Key]);
        }

        if (service.Resource.Name == PlatformServices.Tenancy.Key)
        {
            // Local development applies a new tenant's migrations by running the Migrator project for that tenant.
            var migratorProject = Path.GetFullPath("../Migrator/Migrator.csproj", AppHostDirectory);
            service = service
                .WithEnvironment(ConfigKeys.Onboarding.MigratorCommand, "dotnet")
                .WithEnvironment(ConfigKeys.Onboarding.MigratorArguments, $"run --project {migratorProject} --no-launch-profile -- --tenant {{tenant}}");
        }
        return service;
    }

    /// <summary>
    /// Points every service (and the Gateway) at the token server's internal address for discovery, JWKS and token requests,
    /// so nothing inside the network depends on the public <c>login.</c> hostname resolving.
    /// </summary>
    public static void WireTokenServer(IReadOnlyList<IResourceBuilder<ProjectResource>> services, IResourceBuilder<ProjectResource> gateway)
    {
        var identity = services.Single(s => s.Resource.Name == PlatformServices.Identity.Key);
        var endpoint = identity.GetEndpoint("http");
        foreach (var project in services.Concat([gateway]))
        {
            project
                .WithEnvironment(ConfigKeys.Auth.MetadataAddress, ReferenceExpression.Create($"{endpoint}/.well-known/openid-configuration"))
                .WithEnvironment(ConfigKeys.Auth.TokenEndpoint, ReferenceExpression.Create($"{endpoint}/connect/token"))
                .WithEnvironment(ConfigKeys.Auth.RequireHttpsMetadata, "false");
        }
    }

    /// <summary>The Gateway: the only public entry point. Local development publishes it on HTTPS at <c>https://{tenant}.localhost:{PublicPort}</c>.</summary>
    public IResourceBuilder<ProjectResource> AddGateway(IDistributedApplicationBuilder builder, IReadOnlyList<IResourceBuilder<ProjectResource>> services)
    {
        var gateway = ConfigureDatabase(builder.AddProject("gateway", "../Gateway/Gateway.csproj"))
            .WithHttpsEndpoint(port: PublicPort, name: "https")
            .WithExternalHttpEndpoints()
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain)
            .WithEnvironment(ConfigKeys.Tenancy.PublicPort, PublicPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment(ConfigKeys.Redis.ConnectionString, Redis)
            .WithEnvironment(ConfigKeys.DataProtection.KeyEncryptionKey, _secrets.DataProtectionKey)
            .WithEnvironment(ConfigKeys.Bff.ClientSecret, _secrets.BffSecret)
            .WithHttpHealthCheck("/health/ready")
            .WaitFor(Redis)
            .WaitForCompletion(Migrator);
        foreach (var service in services)
            gateway = gateway.WaitFor(service).WithReference(service);   // WithReference lets the Gateway's service discovery resolve http://{service-key}

        // A certificate for *.localhost (made by scripts/dev-up with mkcert) lets browsers trust the __Host- cookies.
        if (builder.Configuration["Dev:CertificatePath"] is { Length: > 0 } certificate && builder.Configuration["Dev:CertificateKeyPath"] is { Length: > 0 } key)
        {
            gateway = gateway
                .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__Path", certificate)
                .WithEnvironment("ASPNETCORE_Kestrel__Certificates__Default__KeyPath", key);
        }
        return gateway;
    }

    private IResourceBuilder<ProjectResource> ConfigureDatabase(IResourceBuilder<ProjectResource> project) =>
        project
            .WithEnvironment(ConfigKeys.Database.Provider, Provider)
            .WithEnvironment(ConfigKeys.Database.CatalogConnectionString, Catalog)
            .WithEnvironment(ConfigKeys.Database.Servers(DefaultServerPool), TenantServer)
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain);
}

/// <summary>The secrets the stack needs, generated once and persisted in the AppHost's user secrets.</summary>
internal sealed class Secrets
{
    public required IResourceBuilder<ParameterResource> SigningKey { get; init; }

    public required IResourceBuilder<ParameterResource> EncryptionKey { get; init; }

    public required IResourceBuilder<ParameterResource> DataProtectionKey { get; init; }

    public required IResourceBuilder<ParameterResource> BffSecret { get; init; }

    /// <summary>One client secret per service, by service key.</summary>
    public required IReadOnlyDictionary<string, IResourceBuilder<ParameterResource>> ServiceSecrets { get; init; }

    public static Secrets Add(IDistributedApplicationBuilder builder)
    {
        var generated = new GenerateParameterDefault { MinLength = 40, Special = false };
        return new Secrets
        {
            SigningKey = builder.AddParameter("token-signing-key", new EcSigningKeyDefault(), secret: true, persist: true),
            EncryptionKey = builder.AddParameter("token-encryption-key", new Base64KeyDefault(), secret: true, persist: true),
            DataProtectionKey = builder.AddParameter("data-protection-key", new Base64KeyDefault(), secret: true, persist: true),
            BffSecret = builder.AddParameter("bff-client-secret", generated, secret: true, persist: true),
            ServiceSecrets = PlatformServices.All.ToDictionary(
                s => s.Key,
                s => builder.AddParameter($"client-secret-{s.Key}", generated, secret: true, persist: true)),
        };
    }
}
