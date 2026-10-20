using Farm360.Core.Composition;
using Farm360.Core.Data;
using Farm360.Core.Localization;
using Farm360.Core.Platform;
using Farm360.Sample.Api;
using Farm360.Sample.Application;
using Farm360.Sample.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Farm360.Sample;

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
