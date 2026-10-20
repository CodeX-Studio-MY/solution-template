using Farm360.Core.Authorization;
using Farm360.Core.Http;
using Farm360.Core.Platform;
using Farm360.Sample.Application;
using Farm360.Sample.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Farm360.Sample.Api;

/// <summary>
/// The HTTP surface, mounted under <c>/api/{module-key}</c> by the platform. Every endpoint requires a signed-in principal (the
/// fallback policy); <c>RequireModuleRole</c> says which role in this module is the least that may call it.
/// </summary>
internal static class SampleEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var items = api.MapGroup("/v1/items").WithTags("items");

        items.MapGet("", (ItemService service, [AsParameters] PageRequest page, CancellationToken ct) => service.ListAsync(page, ct))
            .RequireModuleRole(Modules.Sample, Role.User);

        items.MapGet("/{id:guid}", async (Guid id, ItemService service, HttpResponse response, CancellationToken ct) => With(response, await service.GetAsync(id, ct)))
            .RequireModuleRole(Modules.Sample, Role.User);

        // Create endpoints accept an Idempotency-Key: a repeated request returns the original result and never creates twice.
        items.MapPost("", async (SaveItemInput body, ItemService service, HttpResponse response, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(body, ct);
            response.Headers.ETag = created.ETag;
            return Results.Created($"{PlatformServices.Sample.RoutePrefix}/v1/items/{created.Id}", created);
        }).RequireModuleRole(Modules.Sample, Role.User).RequireIdempotency<SampleDbContext>().Produces<ItemDto>(StatusCodes.Status201Created);

        // Changes carry the version the client last saw (If-Match): 428 if missing, 412 if someone else changed it first.
        items.MapPut("/{id:guid}", async (Guid id, SaveItemInput body, [FromHeader(Name = "If-Match")] string? ifMatch, ItemService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.RenameAsync(id, ifMatch, body, ct)))
            .RequireModuleRole(Modules.Sample, Role.User);

        items.MapDelete("/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, ItemService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ifMatch, ct);
            return Results.NoContent();
        }).RequireModuleRole(Modules.Sample, Role.Manager).Produces(StatusCodes.Status204NoContent);
    }

    private static ItemDto With(HttpResponse response, ItemDto dto)
    {
        response.Headers.ETag = dto.ETag;
        return dto;
    }
}
