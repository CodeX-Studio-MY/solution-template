using Saaskit.Core.Composition;
using Saaskit.Core.Data;
using Saaskit.Core.Localization;
using Saaskit.Core.Messaging;
using Saaskit.Core.Platform;
using Saaskit.Sample.Api;
using Saaskit.Sample.Application;
using Saaskit.Sample.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Saaskit.Sample;

/// <summary>The module's entry point. A host (or AllInOne.Host) registers it with <c>AddModule&lt;SampleModule&gt;()</c>.</summary>
public sealed class SampleModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.Sample;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<SampleDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(SampleModule).Assembly);
        services.AddModuleOutbox<SampleDbContext>(Service.Schema);   // the relay publishes the events the module saves (ItemCreated)
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ItemService>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => SampleEndpoints.Map(api);
}
