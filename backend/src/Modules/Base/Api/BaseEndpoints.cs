using Microsoft.AspNetCore.Routing;

namespace Saaskit.Base.Api;

/// <summary>
/// The HTTP surface, mounted under <c>/api/base</c> by the platform. The module ships empty: a product adds its master data here
/// (see docs/adding-a-master-entity.md), each endpoint with <c>RequireModuleRole(Modules.Base, ...)</c> saying the least role that may call it.
/// </summary>
internal static class BaseEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
    }
}
