using Acme.Core.Errors;
using Acme.Core.Http;
using Acme.Core.Platform;
using Acme.Sample.Domain;

namespace Acme.Sample.Application;

public sealed record ItemDto(Guid Id, string Name, DateTime CreatedOn, string ETag);

public sealed record SaveItemInput(string Name);

/// <summary>This module's error catalog: stable codes with a status and a message key. Frontend error-code types are generated from it.</summary>
[ErrorCatalog(Modules.Sample)]
public static class SampleErrors
{
    public static readonly ErrorCode NotFound = new("sample-key.not-found", 404, "errors.sampleCamel.notFound");
}

/// <summary>The module's store, as the use cases see it. EF Core stays in Infrastructure.</summary>
public interface IItemRepository
{
    Task<Page<Item>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<Item?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(Item item);

    string ETagOf(Item item);

    /// <summary>Applies the client's <c>If-Match</c> before a change. 428 if missing; 412 on save if stale.</summary>
    void ApplyIfMatch(Item item, string? ifMatch);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>The use cases. Every business rule is in the domain or here; endpoints only translate HTTP.</summary>
public sealed class ItemService(IItemRepository items, TimeProvider time)
{
    public async Task<Page<ItemDto>> ListAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        (await items.ListAsync(page, cancellationToken)).Map(ToDto);

    public async Task<ItemDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToDto(await items.FindAsync(id, cancellationToken) ?? throw new NotFoundException(SampleErrors.NotFound));

    public async Task<ItemDto> CreateAsync(SaveItemInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var item = new Item(input.Name);
        items.Add(item);
        await items.SaveAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task<ItemDto> RenameAsync(Guid id, string? ifMatch, SaveItemInput input, CancellationToken cancellationToken = default)
    {
        Validate(input);
        var item = await items.FindAsync(id, cancellationToken) ?? throw new NotFoundException(SampleErrors.NotFound);
        items.ApplyIfMatch(item, ifMatch);
        item.Rename(input.Name);
        await items.SaveAsync(cancellationToken);
        return ToDto(item);
    }

    public async Task DeleteAsync(Guid id, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var item = await items.FindAsync(id, cancellationToken) ?? throw new NotFoundException(SampleErrors.NotFound);
        items.ApplyIfMatch(item, ifMatch);
        item.MarkDeleted(time.GetUtcNow().UtcDateTime);
        await items.SaveAsync(cancellationToken);
    }

    private static void Validate(SaveItemInput input) =>
        new ValidationBuilder().Required("name", input.Name, "Name").MaxLength("name", input.Name, Item.MaxNameLength, "Name").ThrowIfAny();

    private ItemDto ToDto(Item item) => new(item.Id, item.Name, item.CreatedOn, items.ETagOf(item));
}
