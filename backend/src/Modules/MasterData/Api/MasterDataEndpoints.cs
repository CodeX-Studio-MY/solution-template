using Farm360.Core.Authorization;
using Farm360.Core.Http;
using Farm360.Core.Platform;
using Farm360.MasterData.Application;
using Farm360.MasterData.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Farm360.MasterData.Api;

/// <summary>
/// The HTTP surface, mounted under <c>/api/{module-key}</c> by the platform. Every endpoint requires a signed-in principal (the
/// fallback policy); <c>RequireModuleRole</c> says which role in this module is the least that may call it.
/// </summary>
internal static class MasterDataEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var farms = api.MapGroup("/v1/farms").WithTags("farms");

        farms.MapGet("", (FarmService service, [AsParameters] PageRequest page, CancellationToken ct) => service.ListAsync(page, ct))
            .RequireModuleRole(Modules.MasterData, Role.User);

        farms.MapGet("/{id:guid}", async (Guid id, FarmService service, HttpResponse response, CancellationToken ct) => With(response, await service.GetAsync(id, ct)))
            .RequireModuleRole(Modules.MasterData, Role.User);

        // Create endpoints accept an Idempotency-Key: a repeated request returns the original result and never creates twice.
        farms.MapPost("", async (SaveFarmInput body, FarmService service, HttpResponse response, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(body, ct);
            response.Headers.ETag = created.ETag;
            return Results.Created($"{PlatformServices.MasterData.RoutePrefix}/v1/farms/{created.Id}", created);
        }).RequireModuleRole(Modules.MasterData, Role.User).RequireIdempotency<MasterDataDbContext>().Produces<FarmDto>(StatusCodes.Status201Created);

        // Changes carry the version the client last saw (If-Match): 428 if missing, 412 if someone else changed it first.
        farms.MapPut("/{id:guid}", async (Guid id, SaveFarmInput body, [FromHeader(Name = "If-Match")] string? ifMatch, FarmService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.UpdateAsync(id, ifMatch, body, ct)))
            .RequireModuleRole(Modules.MasterData, Role.User);

        farms.MapDelete("/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, FarmService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ifMatch, ct);
            return Results.NoContent();
        }).RequireModuleRole(Modules.MasterData, Role.Manager).Produces(StatusCodes.Status204NoContent);
    }

    private static FarmDto With(HttpResponse response, FarmDto dto)
    {
        response.Headers.ETag = dto.ETag;
        return dto;
    }
}
