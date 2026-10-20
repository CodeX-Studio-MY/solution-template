using Farm360.Core.Platform;

namespace Farm360.AppHost;

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

    private const string AzuriteAccount = "devstoreaccount1";
    private const string AzuriteKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    /// <summary>The generated, persisted secrets shared by the stack. Values never appear in the repository.</summary>
    private readonly Secrets _secrets;

    /// <summary>Points a project at the object store / the mail catcher this AppHost started.</summary>
    private readonly Action<IResourceBuilder<ProjectResource>> _configureStorage;
    private readonly Action<IResourceBuilder<ProjectResource>> _configureMail;

    private PlatformInfrastructure(
        string provider,
        string baseDomain,
        IResourceBuilder<IResourceWithConnectionString> catalog,
        IResourceBuilder<IResourceWithConnectionString> tenantServer,
        IResourceBuilder<IResourceWithConnectionString> redis,
        IResourceBuilder<IResourceWithConnectionString> rabbitMq,
        IResourceBuilder<ProjectResource> migrator,
        Action<IResourceBuilder<ProjectResource>> configureStorage,
        Action<IResourceBuilder<ProjectResource>> configureMail,
        Secrets secrets)
    {
        _secrets = secrets;
        _configureStorage = configureStorage;
        _configureMail = configureMail;
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

        // Object storage emulator for the cloud in platform.yaml: Azurite for azure, SeaweedFS (S3) for aws. Local and compose runs
        // use the same containers; the services get the same Storage:* settings a cloud deployment would (different values).
        Action<IResourceBuilder<ProjectResource>> configureStorage;
        string cloud = PlatformInfo.Cloud;
        switch (cloud)
        {
            case "azure":
                var azurite = builder.AddContainer("azurite", "mcr.microsoft.com/azure-storage/azurite", "3.37.0")
                    .WithEntrypoint("azurite-blob")
                    .WithArgs("--blobHost", "0.0.0.0", "--location", "/data", "--skipApiVersionCheck")
                    .WithVolume(Volume("azurite-data"), "/data")
                    .WithHttpEndpoint(targetPort: 10000, name: "blob");
                var blob = azurite.GetEndpoint("blob");
                // Azurite's well-known development account; the key is public and only valid against the emulator.
                var connectionString = ReferenceExpression.Create($"DefaultEndpointsProtocol=http;AccountName={AzuriteAccount};AccountKey={AzuriteKey};BlobEndpoint={blob}/{AzuriteAccount};");
                configureStorage = project => project
                    .WithEnvironment(ConfigKeys.Storage.Provider, "azure")
                    .WithEnvironment(ConfigKeys.Storage.ConnectionString, connectionString)
                    .WaitFor(azurite);
                break;
            case "aws":
                var seaweed = builder.AddSeaweedFS("seaweedfs").WithS3().WithDataVolume(Volume("seaweedfs-data"));
                configureStorage = project => project
                    .WithEnvironment(ConfigKeys.Storage.Provider, "aws")
                    .WithEnvironment(ConfigKeys.Storage.ServiceUrl, ReferenceExpression.Create($"{seaweed.Resource.PrimaryEndpoint}"))
                    .WithEnvironment(ConfigKeys.Storage.Region, "us-east-1")
                    .WithEnvironment(ConfigKeys.Storage.AccessKey, seaweed.Resource.AccessKey)
                    .WithEnvironment(ConfigKeys.Storage.SecretKey, seaweed.Resource.SecretKey)
                    .WaitFor(seaweed);
                break;
            default:
                throw new InvalidOperationException($"Unknown cloud '{cloud}'.");
        }

        var mailpit = builder.AddMailPit("mailpit")
            .PublishAsDockerComposeService((_, service) => service.Ports.Add($"{MailpitUiHostPort}:8025"));
        Action<IResourceBuilder<ProjectResource>> configureMail = project => project
            .WithEnvironment(ConfigKeys.Smtp.Host, mailpit.Resource.Host)
            .WithEnvironment(ConfigKeys.Smtp.Port, mailpit.Resource.Port)
            .WithEnvironment(ConfigKeys.Smtp.FromAddress, $"no-reply@{baseDomain}")
            .WaitFor(mailpit);

        var migrator = builder.AddProject("migrator", "../Migrator/Migrator.csproj")
            .WithArgs("--catalog", "--all");

        var infrastructure = new PlatformInfrastructure(provider, baseDomain, catalog, server, redis, rabbitMq, migrator, configureStorage, configureMail, Secrets.Add(builder));
        infrastructure.ConfigureDatabase(migrator).WaitFor(catalog);
        return infrastructure;
    }

    /// <summary>Stable volume names, so the published compose file is the same on every machine.</summary>
    private static string Volume(string name) => $"{PlatformInfo.Product}-{name}";

    /// <summary>Gives a service host its configuration and starts it after the Migrator has finished.</summary>
    public IResourceBuilder<ProjectResource> ConfigureService(IResourceBuilder<ProjectResource> service) =>
        ConfigureService(service, [PlatformServices.All.Single(s => s.Key == service.Resource.Name)]);

    /// <summary>
    /// One process running all services (<c>AllInOne.Host</c>). It presents itself as the first service (its client credentials),
    /// and the Gateway routes every <c>/api</c> request to it.
    /// </summary>
    public IResourceBuilder<ProjectResource> AddAllInOne(IDistributedApplicationBuilder builder)
    {
        var host = builder.AddProject("allinone", "../Hosts/AllInOne.Host/AllInOne.Host.csproj")
            .WithHttpEndpoint(port: PlatformServices.All[0].Port, name: "http");
        host = ConfigureService(host, PlatformServices.All);
        return host.WithEnvironment(ConfigKeys.ServiceClient.LocalAddress, host.GetEndpoint("http"));
    }

    private IResourceBuilder<ProjectResource> ConfigureService(IResourceBuilder<ProjectResource> service, IReadOnlyList<PlatformService> hosted, bool http = true)
    {
        service = ConfigureDatabase(service)
            .WithEnvironment(ConfigKeys.Tenancy.BaseDomain, BaseDomain)
            .WithEnvironment(ConfigKeys.Tenancy.PublicPort, PublicPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .WithEnvironment(ConfigKeys.Redis.ConnectionString, Redis)
            .WithEnvironment(ConfigKeys.DataProtection.KeyEncryptionKey, _secrets.DataProtectionKey)
            .WithEnvironment(ConfigKeys.ServiceClient.ClientSecret, _secrets.ServiceSecrets[hosted[0].Key])
            .WaitFor(Redis)
            .WaitFor(RabbitMq)
            .WaitForCompletion(Migrator);
        if (http)
            service = service.WithHttpHealthCheck("/health/ready");
        WithMessagingAndFiles(service);

        if (hosted.Contains(PlatformServices.Identity))
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

        // Local development applies a new tenant's migrations by running the Migrator project for that tenant. Only when running: the
        // path is this machine's, so it has no place in the published compose file (which must be the same on every machine).
        if (hosted.Contains(PlatformServices.Tenancy) && service.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            var migratorProject = Path.GetFullPath("../Migrator/Migrator.csproj", AppHostDirectory);
            service = service
                .WithEnvironment(ConfigKeys.Onboarding.MigratorCommand, "dotnet")
                .WithEnvironment(ConfigKeys.Onboarding.MigratorArguments, $"run --project \"{migratorProject}\" --no-launch-profile -- --tenant {{tenant}}");
        }
        return service;
    }

    /// <summary>
    /// Development only: onboards the demo tenants and seeds their sample data once the token server is up, then exits. It runs with
    /// the configuration of the services it stands in for (Tenancy's onboarding calls Identity and runs the Migrator).
    /// </summary>
    public IResourceBuilder<ProjectResource> AddDevSeed(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> tokenServer)
    {
        var seed = ConfigureService(builder.AddProject("dev-seed", "../DevSeed/DevSeed.csproj"), PlatformServices.All, http: false)
            .WithEnvironment(ConfigKeys.ServiceClient.LocalAddress, tokenServer.GetEndpoint("http"))
            // Token requests go straight to the token server, like every service's, not out through the public Gateway.
            .WithEnvironment(ConfigKeys.Auth.MetadataAddress, ReferenceExpression.Create($"{tokenServer.GetEndpoint("http")}/.well-known/openid-configuration"))
            .WithEnvironment(ConfigKeys.Auth.TokenEndpoint, ReferenceExpression.Create($"{tokenServer.GetEndpoint("http")}/connect/token"))
            .WithEnvironment(ConfigKeys.Auth.RequireHttpsMetadata, "false")
            .WaitFor(tokenServer);
        if (builder.Configuration["DevSeed:InvitationsFile"] is { Length: > 0 } invitations)
            seed = seed.WithEnvironment("DevSeed__InvitationsFile", invitations);
        return seed;
    }

    /// <summary>RabbitMQ, the object store and the mail catcher: what a host needs beyond the database and Valkey.</summary>
    private void WithMessagingAndFiles(IResourceBuilder<ProjectResource> project)
    {
        project.WithEnvironment(ConfigKeys.RabbitMq.ConnectionString, RabbitMq);
        _configureStorage(project);
        _configureMail(project);
    }

    /// <summary>
    /// The one clustered Scheduler: recurring jobs run here and nowhere else. It publishes events and keeps its job store in the catalog.
    /// </summary>
    public IResourceBuilder<ProjectResource> AddScheduler(IDistributedApplicationBuilder builder)
    {
        var scheduler = ConfigureDatabase(builder.AddProject("scheduler", "../Scheduler/Scheduler.csproj").WithHttpEndpoint(name: "http"))
            .WithEnvironment(ConfigKeys.Redis.ConnectionString, Redis)
            .WithEnvironment(ConfigKeys.DataProtection.KeyEncryptionKey, _secrets.DataProtectionKey)
            .WithHttpHealthCheck("/health/ready")
            .WaitFor(Redis)
            .WaitFor(RabbitMq)
            .WaitForCompletion(Migrator);
        WithMessagingAndFiles(scheduler);
        return scheduler;
    }

    /// <summary>
    /// Points every service (and the Gateway) at the token server's internal address for discovery, JWKS and token requests,
    /// so nothing inside the network depends on the public <c>login.</c> hostname resolving.
    /// </summary>
    public static void WireTokenServer(IReadOnlyList<IResourceBuilder<ProjectResource>> services, IResourceBuilder<ProjectResource> tokenServer, IResourceBuilder<ProjectResource> gateway)
    {
        var endpoint = tokenServer.GetEndpoint("http");
        foreach (var project in services.Concat([gateway]))
        {
            project
                .WithEnvironment(ConfigKeys.Auth.MetadataAddress, ReferenceExpression.Create($"{endpoint}/.well-known/openid-configuration"))
                .WithEnvironment(ConfigKeys.Auth.TokenEndpoint, ReferenceExpression.Create($"{endpoint}/connect/token"))
                .WithEnvironment(ConfigKeys.Auth.RequireHttpsMetadata, "false");
        }
    }

    /// <summary>The Gateway: the only public entry point. Local development publishes it on HTTPS at <c>https://{tenant}.localhost:{PublicPort}</c>.</summary>
    public IResourceBuilder<ProjectResource> AddGateway(IDistributedApplicationBuilder builder, IReadOnlyList<IResourceBuilder<ProjectResource>> services, bool allInOne = false)
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
        if (allInOne)
            gateway = gateway.WithEnvironment(ConfigKeys.Bff.AllInOneAddress, services.Single().GetEndpoint("http"));

        // The built frontend (scripts/dev-up builds it): the Gateway serves the shell and the module bundles like a CDN would.
        if (builder.Configuration["Dev:FrontendPath"] is { Length: > 0 } frontend)
        {
            gateway = gateway
                .WithEnvironment(ConfigKeys.Bff.ShellRoot, Path.Combine(frontend, "shell"))
                .WithEnvironment(ConfigKeys.Bff.ModulesRoot, Path.Combine(frontend, "mfe"));
        }

        // A certificate for *.localhost (made by scripts/dev-up with mkcert) lets browsers trust the __Host- cookies.
        if (builder.Configuration["Dev:CertificatePath"] is { Length: > 0 } certificate && builder.Configuration["Dev:CertificateKeyPath"] is { Length: > 0 } key)
        {
            // Aspire's certificate API is still experimental; it is the supported way to replace the ASP.NET development certificate.
#pragma warning disable ASPIRECERTIFICATES001
            gateway = gateway.WithHttpsCertificate(System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(certificate, key));
#pragma warning restore ASPIRECERTIFICATES001
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
