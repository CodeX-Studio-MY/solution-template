using Acme.Core.Platform;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Acme.Core.Composition;

/// <summary>
/// A module's entry point. A host (or <c>AllInOne.Host</c>) adds modules with <c>AddModule&lt;T&gt;()</c>;
/// the module registers its services and maps its endpoints under its service's route prefix.
/// </summary>
public interface IPlatformModule
{
    /// <summary>The generated service this module belongs to (key, route prefix, schema, audience).</summary>
    static abstract PlatformService Service { get; }

    /// <summary>Registers the module's services, DbContext and options.</summary>
    static abstract void Register(IHostApplicationBuilder builder);

    /// <summary>Maps the module's endpoints. <paramref name="api"/> is already under <c>/api/{service-key}</c>.</summary>
    static abstract void MapEndpoints(IEndpointRouteBuilder api);
}
