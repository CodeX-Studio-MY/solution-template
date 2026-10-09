using Acme.Core.Composition;
using Acme.Core.Data;
using Acme.Core.Localization;
using Acme.Core.Platform;
using Acme.Sample.Api;
using Acme.Sample.Application;
using Acme.Sample.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Acme.Sample;

/// <summary>The module's entry point. A host (or AllInOne.Host) registers it with <c>AddModule&lt;SampleModule&gt;()</c>.</summary>
public sealed class SampleModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Sample;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<SampleDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(SampleModule).Assembly);
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ItemService>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => SampleEndpoints.Map(api);
}
