using Acme.Core.Authorization;
using Acme.Core.Http;
using Acme.Core.Platform;
using Acme.Core.Security;
using Acme.PurchaseRequest.Application;
using Acme.PurchaseRequest.Domain;
using Acme.PurchaseRequest.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Acme.PurchaseRequest.Api;

internal static class PurchaseRequestEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var v1 = api.MapGroup("/v1");
        var requests = v1.MapGroup("/requests").WithTags("requests");

        requests.MapGet("", (RequestService service, [AsParameters] PageRequest page, RequestStatus? status, bool? mine, CancellationToken ct) =>
            service.ListAsync(page, status, mine ?? false, ct)).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        requests.MapPost("", async (SaveRequestInput body, RequestService service, HttpResponse response, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(body, ct);
            response.Headers.ETag = created.ETag;
            return Results.Created($"{PlatformServices.PurchaseRequest.RoutePrefix}/v1/requests/{created.Id}", created);
        }).RequireModuleRole(Modules.PurchaseRequest, Role.User).RequireIdempotency<PurchaseRequestDbContext>();

        requests.MapGet("/{id:guid}", async (Guid id, RequestService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.GetAsync(id, ct))).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        requests.MapPut("/{id:guid}", async (Guid id, SaveRequestInput body, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.UpdateAsync(id, ifMatch, body, ct))).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        requests.MapDelete("/{id:guid}", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ifMatch, ct);
            return Results.NoContent();
        }).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        requests.MapPost("/{id:guid}/submit", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.SubmitAsync(id, ifMatch, ct))).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        requests.MapPost("/{id:guid}/approve", async (Guid id, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.ApproveAsync(id, ifMatch, ct))).RequirePermission(PurchaseRequestPermissions.Approve);

        requests.MapPost("/{id:guid}/reject", async (Guid id, RejectInput body, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, HttpResponse response, CancellationToken ct) =>
            With(response, await service.RejectAsync(id, ifMatch, body, ct))).RequirePermission(PurchaseRequestPermissions.Approve);

        requests.MapPost("/{id:guid}/quote", async (Guid id, IFormFile file, [FromHeader(Name = "If-Match")] string? ifMatch, RequestService service, HttpResponse response, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return With(response, await service.AttachQuoteAsync(id, ifMatch, stream, file.FileName, ct));
        }).RequireModuleRole(Modules.PurchaseRequest, Role.User).DisableAntiforgery();

        requests.MapGet("/{id:guid}/quote", async (Guid id, RequestService service, CancellationToken ct) =>
            new QuoteLink((await service.QuoteDownloadUrlAsync(id, ct)).ToString())).RequireModuleRole(Modules.PurchaseRequest, Role.User);

        // A scanner or other device at the loading dock confirms delivery. Only machines may call it.
        v1.MapPost("/goods-received", async (GoodsReceivedInput body, RequestService service, CancellationToken ct) => await service.ReceiveGoodsAsync(body.RequestId, ct))
            .RequireModuleRole(Modules.PurchaseRequest, Role.User)
            .AllowPrincipalTypes(PrincipalType.Machine)
            .WithTags("devices");
    }

    private static RequestDto With(HttpResponse response, RequestDto dto)
    {
        response.Headers.ETag = dto.ETag;
        return dto;
    }

    internal sealed record QuoteLink(string Url);

    internal sealed record GoodsReceivedInput(Guid RequestId);
}
