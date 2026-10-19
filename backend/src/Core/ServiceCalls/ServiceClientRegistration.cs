using Saaskit.Core.Composition;
using Saaskit.Core.Configuration;
using Saaskit.Core.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Saaskit.Core.ServiceCalls;

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
        return services.AddHttpClient<TClient, TImplementation>((sp, client) => client.BaseAddress = AddressOf(sp, target))
            .AddHttpMessageHandler(sp => ActivatorUtilities.CreateInstance<PlatformTokenHandler>(sp, target));
    }

    /// <summary>
    /// A service this host runs itself (<c>AllInOne.Host</c>) is called at this process's own address; any other by name.
    /// Either way the call goes through HTTP with a token for the target's audience, so a module behaves the same in both layouts.
    /// </summary>
    private static Uri AddressOf(IServiceProvider services, PlatformService target)
    {
        var hosted = services.GetRequiredService<HostedPlatformServices>();
        if (!hosted.All.Contains(target))
            return new Uri($"http://{target.Key}");
        var local = services.GetRequiredService<IOptions<ServiceClientOptions>>().Value.LocalAddress;
        return new Uri(local ?? $"http://localhost:{hosted.All[0].Port}");
    }

    /// <summary>Binds <c>ServiceClient:*</c>. Called by AddPlatform.</summary>
    public static IServiceCollection AddServiceClientOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddPlatformOptions<ServiceClientOptions>(configuration);
        return services;
    }
}
