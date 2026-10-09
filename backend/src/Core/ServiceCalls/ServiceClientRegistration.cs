using Acme.Core.Configuration;
using Acme.Core.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Acme.Core.ServiceCalls;

public static class ServiceClientRegistration
{
    /// <summary>
    /// Registers a typed HTTP client for another service. It resolves the service by name (Aspire service discovery locally,
    /// platform DNS in the cloud; never a hard-coded host), uses the standard resilience handler, and attaches the right token.
    /// </summary>
    public static IHttpClientBuilder AddPlatformServiceClient<TClient, TImplementation>(this IServiceCollection services, PlatformService target)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.AddSingleton<PlatformTokenProvider>();
        services.AddHttpClient(PlatformTokenProvider.HttpClientName);
        return services.AddHttpClient<TClient, TImplementation>(client => client.BaseAddress = new Uri($"http://{target.Key}"))
            .AddHttpMessageHandler(sp => ActivatorUtilities.CreateInstance<PlatformTokenHandler>(sp, target));
    }

    /// <summary>Binds <c>ServiceClient:*</c>. Called by AddPlatform.</summary>
    public static IServiceCollection AddServiceClientOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<ServiceClientOptions>(configuration);
        return services;
    }
}
