using Farm360.Core.Configuration;
using Farm360.Core.Platform;
using Yarp.ReverseProxy.Configuration;

namespace Farm360.Gateway.Proxy;

/// <summary>
/// The reverse-proxy routes and clusters, built from the services in platform.yaml (through the generated
/// <see cref="PlatformServices"/>), so adding a service never needs a routing edit.
/// </summary>
internal static class ProxyConfiguration
{
    public const string AudienceMetadata = "Audience";
    public const string LoginRoute = "login";

    public static (IReadOnlyList<RouteConfig> Routes, IReadOnlyList<ClusterConfig> Clusters) Build(TenancyOptions tenancy, BffOptions bff)
    {
        var routes = new List<RouteConfig>();
        var clusters = new List<ClusterConfig>();

        foreach (var service in PlatformServices.All)
        {
            // Service discovery resolves the name (Aspire locally, platform DNS in the cloud); never a hard-coded host.
            var address = bff.AllInOneAddress ?? $"http://{service.Key}";
            clusters.Add(new ClusterConfig
            {
                ClusterId = service.Key,
                Destinations = new Dictionary<string, DestinationConfig> { ["primary"] = new() { Address = address } },
            });
            routes.Add(new RouteConfig
            {
                RouteId = service.Key,
                ClusterId = service.Key,
                Order = 10,
                Match = new RouteMatch { Path = $"{service.RoutePrefix}/{{**rest}}" },
                Metadata = new Dictionary<string, string> { [AudienceMetadata] = service.Audience },
            });
        }

        // The central sign-in host: every path goes to the token server (Identity). No session, no token.
        routes.Add(new RouteConfig
        {
            RouteId = LoginRoute,
            ClusterId = PlatformServices.Identity.Key,
            Order = 0,
            Match = new RouteMatch { Hosts = [$"login.{tenancy.BaseDomain}"], Path = "/{**rest}" },
        });
        return (routes, clusters);
    }
}
