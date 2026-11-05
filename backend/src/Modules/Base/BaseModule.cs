using Saaskit.Core.Composition;
using Saaskit.Core.Data;
using Saaskit.Core.Localization;
using Saaskit.Core.Platform;
using Saaskit.Base.Api;
using Saaskit.Base.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace Saaskit.Base;

/// <summary>The module's entry point. A host (or AllInOne.Host) registers it with <c>AddModule&lt;BaseModule&gt;()</c>.</summary>
public sealed class BaseModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Base;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<BaseDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(BaseModule).Assembly);
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => BaseEndpoints.Map(api);
}
