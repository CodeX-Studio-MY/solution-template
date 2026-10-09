using System.Text.RegularExpressions;
using Acme.Core.Customization;
using Acme.Core.Errors;
using Acme.Core.Platform;

namespace Acme.PurchaseRequest.Application;

/// <summary>Defines the tenant's custom fields for the module's entities (rung 4). Reading is for anyone who uses the screens; changing is for admins.</summary>
public sealed partial class CustomFieldService(ICustomFieldStore store)
{
    /// <summary>The entities that can carry custom fields. Today the request itself, named like its module.</summary>
    private static readonly string[] Entities = [Modules.PurchaseRequest];

    public async Task<IReadOnlyList<CustomFieldDto>> ListAsync(string entity, CancellationToken cancellationToken = default)
    {
        CheckEntity(entity);
        return [.. (await store.ListAsync(entity, cancellationToken)).OrderBy(d => d.DisplayOrder).ThenBy(d => d.Key, StringComparer.Ordinal).Select(ToDto)];
    }

    public async Task<CustomFieldDto> SaveAsync(string entity, string key, SaveCustomFieldInput input, CancellationToken cancellationToken = default)
    {
        CheckEntity(entity);
        var errors = new ValidationBuilder();
        if (!KeyPattern().IsMatch(key) || key.Length > CustomFieldDefinition.MaxKeyLength)
            errors.Add("key", "validation.invalid", "Key");
        errors.Required("label", input.Label, "Label").MaxLength("label", input.Label, 200, "Label");
        if (!Enum.TryParse<CustomFieldType>(input.Type, ignoreCase: true, out var type))
            errors.Add("type", "validation.invalid", "Type");
        else if (type == CustomFieldType.List && (input.Options is null || input.Options.Count == 0 || input.Options.Any(string.IsNullOrWhiteSpace)))
            errors.Add("options", "validation.required", "Options");
        errors.ThrowIfAny();

        var existing = await store.FindAsync(entity, key, cancellationToken);
        if (existing is null)
        {
            existing = new CustomFieldDefinition(entity, key, input.Label.Trim(), type, input.Required, input.Options?.Select(o => o.Trim()), input.DisplayOrder);
            store.Add(existing);
        }
        else
        {
            existing.Update(input.Label.Trim(), type, input.Required, input.Options?.Select(o => o.Trim()), input.DisplayOrder);
        }
        await store.SaveAsync(cancellationToken);
        return ToDto(existing);
    }

    /// <summary>Removes a definition. Values already stored on requests stay in their JSON but are no longer shown or validated.</summary>
    public async Task DeleteAsync(string entity, string key, CancellationToken cancellationToken = default)
    {
        CheckEntity(entity);
        var existing = await store.FindAsync(entity, key, cancellationToken) ?? throw new NotFoundException(PurchaseRequestErrors.NotFound);
        store.Remove(existing);
        await store.SaveAsync(cancellationToken);
    }

    private static void CheckEntity(string entity)
    {
        if (!Entities.Contains(entity, StringComparer.Ordinal))
            throw new NotFoundException(PurchaseRequestErrors.NotFound);
    }

    private static CustomFieldDto ToDto(CustomFieldDefinition d) => new(d.Key, d.Label, d.Type.ToString().ToLowerInvariant(), d.Required, d.Options, d.DisplayOrder);

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex KeyPattern();
}
