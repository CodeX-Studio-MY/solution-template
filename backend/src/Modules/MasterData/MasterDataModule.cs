using Farm360.Core.Composition;
using Farm360.Core.Data;
using Farm360.Core.Localization;
using Farm360.Core.Platform;
using Farm360.MasterData.Api;
using Farm360.MasterData.Application;
using Farm360.MasterData.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Farm360.MasterData;

/// <summary>The module's entry point. A host (or AllInOne.Host) registers it with <c>AddModule&lt;MasterDataModule&gt;()</c>.</summary>
public sealed class MasterDataModule : IPlatformModule
{
    public static PlatformService Service => PlatformServices.MasterData;

    public static void Register(IHostApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddModuleDbContext<MasterDataDbContext>(Service.Schema);
        services.AddModuleMessages(typeof(MasterDataModule).Assembly);
        services.AddScoped<IItemRepository, ItemRepository>();
        services.AddScoped<ItemService>();
    }

    public static void MapEndpoints(IEndpointRouteBuilder api) => MasterDataEndpoints.Map(api);
}
