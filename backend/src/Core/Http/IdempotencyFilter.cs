using System.Security.Cryptography;
using System.Text;
using Farm360.Core.Data;
using Farm360.Core.Errors;
using Farm360.Core.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Farm360.Core.Http;

public static class IdempotencyExtensions
{
    /// <summary>
    /// Makes a create endpoint accept an <c>Idempotency-Key</c> header (following the IETF draft). A repeated key returns the
    /// original response instead of creating a duplicate; the same key with a different body is refused.
    /// </summary>
    public static RouteHandlerBuilder RequireIdempotency<TContext>(this RouteHandlerBuilder builder)
        where TContext : ModuleDbContext =>
        builder.AddEndpointFilter<IdempotencyFilter<TContext>>();
}

internal sealed class IdempotencyFilter<TContext>(ICurrentPrincipal principal, TimeProvider time) : IEndpointFilter
    where TContext : ModuleDbContext
{
    /// <summary>A request that started but never finished (the process died) is treated as abandoned after this long.</summary>
    private static readonly TimeSpan AbandonedAfter = TimeSpan.FromMinutes(2);

    /// <summary>The same JSON the API writes: camelCase, enums as strings.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions ResponseJson = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) },
    };

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[PlatformHeaders.IdempotencyKey].ToString();
        if (string.IsNullOrEmpty(key))
            return await next(context);
        if (key.Length > IdempotencyRecord.MaxKeyLength)
            throw new ValidationException([new FieldError(PlatformHeaders.IdempotencyKey, "validation.maxLength", PlatformHeaders.IdempotencyKey, IdempotencyRecord.MaxKeyLength)]);

        var db = http.RequestServices.GetRequiredService<TContext>();
        var owner = principal.Id ?? "anonymous";
        var hash = await HashBodyAsync(http.Request);
        var now = time.GetUtcNow().UtcDateTime;

        var record = IdempotencyRecord.Start(owner, key, hash, now);
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            db.Entry(record).State = EntityState.Detached;
            var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.PrincipalId == owner && r.Key == key, http.RequestAborted);
            if (existing is null)
                throw;   // a different failure
            if (existing.RequestHash != hash)
                throw new BusinessRuleException(PlatformErrors.IdempotencyKeyReused);
            if (existing.IsCompleted)
                return Replay(http, existing);
            if (now - existing.CreatedOn < AbandonedAfter)
                throw new ConflictException(PlatformErrors.IdempotencyInProgress);
            // Abandoned: clear it and run the request now.
            await db.IdempotencyRecords.Where(r => r.PrincipalId == owner && r.Key == key).ExecuteDeleteAsync(http.RequestAborted);
            return await InvokeAsync(context, next);
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            // The request failed: let the client retry with the same key.
            await db.IdempotencyRecords.Where(r => r.PrincipalId == owner && r.Key == key).ExecuteDeleteAsync(CancellationToken.None);
            throw;
        }

        var (status, body, contentType, location) = Describe(result);
        if (status >= 500)
        {
            await db.IdempotencyRecords.Where(r => r.PrincipalId == owner && r.Key == key).ExecuteDeleteAsync(CancellationToken.None);
            return result;
        }
        record.Complete(status, contentType, body, location);
        await db.SaveChangesAsync(http.RequestAborted);
        return result;
    }

    private static IResult Replay(HttpContext http, IdempotencyRecord record)
    {
        http.Response.Headers["Idempotent-Replayed"] = "true";
        if (record.Location is not null)
            http.Response.Headers.Location = record.Location;
        return Results.Text(record.ResponseBody ?? "", record.ContentType ?? "application/json", Encoding.UTF8, record.StatusCode ?? StatusCodes.Status200OK);
    }

    /// <summary>The status, JSON body and location of what the handler returned, so it can be replayed.</summary>
    private static (int Status, string? Body, string? ContentType, string? Location) Describe(object? result)
    {
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        var value = result is IValueHttpResult valued ? valued.Value : result is IResult ? null : result;
        // Results.Created(...) exposes where the new resource is; there is no common interface for it.
        var location = result?.GetType().GetProperty("Location")?.GetValue(result) as string;
        var body = value is null ? null : System.Text.Json.JsonSerializer.Serialize(value, ResponseJson);
        return (status, body, body is null ? null : "application/json", location);
    }

    private static async Task<string> HashBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();
        // Binding has already read the body; rewind to hash it, then leave it where it was found.
        var position = request.Body.Position;
        request.Body.Position = 0;
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer);
        request.Body.Position = position;
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray()));
    }
}
