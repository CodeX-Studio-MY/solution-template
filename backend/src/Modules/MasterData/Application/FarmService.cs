using Farm360.Core.Errors;
using Farm360.Core.Http;
using Farm360.Core.Platform;
using Farm360.MasterData.Domain;
using NetTopologySuite.Geometries;

namespace Farm360.MasterData.Application;

/// <summary>A farm on the wire. The location is two numbers: a spatial type never reaches the API (docs/adr/0024).</summary>
public sealed record FarmDto(
    Guid Id,
    string Name,
    string? Code,
    FarmType FarmType,
    string? GlobalGapNumber,
    string? Description,
    string? Address,
    string? ContactPerson,
    string? ContactPhone,
    decimal? AreaHectares,
    double? Latitude,
    double? Longitude,
    int OrderSequence,
    bool IsExternal,
    bool IsActive,
    DateTime CreatedOn,
    string ETag);

/// <summary>Create and update body. Left out, a field takes its default (type <c>field</c>, external, active, order 0).</summary>
public sealed record SaveFarmInput(
    string Name,
    string? Code = null,
    FarmType? FarmType = null,
    string? GlobalGapNumber = null,
    string? Description = null,
    string? Address = null,
    string? ContactPerson = null,
    string? ContactPhone = null,
    decimal? AreaHectares = null,
    double? Latitude = null,
    double? Longitude = null,
    int? OrderSequence = null,
    bool? IsExternal = null,
    bool? IsActive = null);

/// <summary>This module's error catalog: stable codes with a status and a message key. Frontend error-code types are generated from it.</summary>
[ErrorCatalog(Modules.MasterData)]
public static class MasterDataErrors
{
    public static readonly ErrorCode NotFound = new("master-data.not-found", 404, "errors.masterData.notFound");
    public static readonly ErrorCode CodeInUse = new("master-data.code-in-use", 409, "errors.masterData.codeInUse");
}

/// <summary>The module's store, as the use cases see it. EF Core stays in Infrastructure.</summary>
public interface IFarmRepository
{
    Task<Page<Farm>> ListAsync(PageRequest page, CancellationToken cancellationToken = default);

    Task<Farm?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Whether a farm that is not deleted already has this code (other than <paramref name="exceptId"/>).</summary>
    Task<bool> CodeInUseAsync(string code, Guid? exceptId, CancellationToken cancellationToken = default);

    void Add(Farm farm);

    string ETagOf(Farm farm);

    /// <summary>Applies the client's <c>If-Match</c> before a change. 428 if missing; 412 on save if stale.</summary>
    void ApplyIfMatch(Farm farm, string? ifMatch);

    Task SaveAsync(CancellationToken cancellationToken = default);
}

/// <summary>The use cases. Every business rule is in the domain or here; endpoints only translate HTTP.</summary>
public sealed class FarmService(IFarmRepository farms, TimeProvider time)
{
    public const decimal MaxAreaHectares = 1_000_000m;

    public async Task<Page<FarmDto>> ListAsync(PageRequest page, CancellationToken cancellationToken = default) =>
        (await farms.ListAsync(page, cancellationToken)).Map(ToDto);

    public async Task<FarmDto> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        ToDto(await FindAsync(id, cancellationToken));

    public async Task<FarmDto> CreateAsync(SaveFarmInput input, CancellationToken cancellationToken = default)
    {
        var details = ToDetails(input);
        await EnsureCodeIsFreeAsync(details.Code, null, cancellationToken);
        var farm = new Farm(details);
        farms.Add(farm);
        await farms.SaveAsync(cancellationToken);
        return ToDto(farm);
    }

    public async Task<FarmDto> UpdateAsync(Guid id, string? ifMatch, SaveFarmInput input, CancellationToken cancellationToken = default)
    {
        var details = ToDetails(input);
        var farm = await FindAsync(id, cancellationToken);
        farms.ApplyIfMatch(farm, ifMatch);
        await EnsureCodeIsFreeAsync(details.Code, id, cancellationToken);
        farm.Update(details);
        await farms.SaveAsync(cancellationToken);
        return ToDto(farm);
    }

    public async Task DeleteAsync(Guid id, string? ifMatch, CancellationToken cancellationToken = default)
    {
        var farm = await FindAsync(id, cancellationToken);
        farms.ApplyIfMatch(farm, ifMatch);
        farm.MarkDeleted(time.GetUtcNow().UtcDateTime);
        await farms.SaveAsync(cancellationToken);
    }

    private async Task<Farm> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await farms.FindAsync(id, cancellationToken) ?? throw new NotFoundException(MasterDataErrors.NotFound);

    // The filtered unique index is the backstop; this check gives the caller a clear 409 instead of a database error.
    private async Task EnsureCodeIsFreeAsync(string? code, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (Farm.NormalizeCode(code) is { } normalized && await farms.CodeInUseAsync(normalized, exceptId, cancellationToken))
            throw new ConflictException(MasterDataErrors.CodeInUse);
    }

    /// <summary>Validates the body and builds the domain's details, converting latitude and longitude to a point.</summary>
    private static FarmDetails ToDetails(SaveFarmInput input)
    {
        var errors = new ValidationBuilder()
            .Required("name", input.Name, "Name")
            .MaxLength("name", input.Name?.Trim(), Farm.MaxNameLength, "Name")
            .MaxLength("code", input.Code?.Trim(), Farm.MaxCodeLength, "Code")
            .MaxLength("globalGapNumber", input.GlobalGapNumber?.Trim(), Farm.MaxGlobalGapNumberLength, "GGN")
            .MaxLength("description", input.Description?.Trim(), Farm.MaxDescriptionLength, "Description")
            .MaxLength("address", input.Address?.Trim(), Farm.MaxAddressLength, "Address")
            .MaxLength("contactPerson", input.ContactPerson?.Trim(), Farm.MaxContactPersonLength, "Contact person")
            .MaxLength("contactPhone", input.ContactPhone?.Trim(), Farm.MaxContactPhoneLength, "Contact phone");

        if (input.FarmType is { } type && !Enum.IsDefined(type))
            errors.Add("farmType", "validation.unknownOption", "Farm type");
        if (input.AreaHectares is < 0 or > MaxAreaHectares)
            errors.Add("areaHectares", "validation.range", "Area", 0, MaxAreaHectares);
        if (input.OrderSequence is < 0)
            errors.Add("orderSequence", "validation.range", "Order", 0, int.MaxValue);

        // A location is both numbers or neither.
        if (input.Latitude is null && input.Longitude is not null)
            errors.Add("latitude", "validation.required", "Latitude");
        if (input.Longitude is null && input.Latitude is not null)
            errors.Add("longitude", "validation.required", "Longitude");
        if (input.Latitude is { } lat && (double.IsNaN(lat) || lat is < -90 or > 90))
            errors.Add("latitude", "validation.range", "Latitude", -90, 90);
        if (input.Longitude is { } lon && (double.IsNaN(lon) || lon is < -180 or > 180))
            errors.Add("longitude", "validation.range", "Longitude", -180, 180);

        errors.ThrowIfAny();

        return new FarmDetails(
            input.Name!, // Required above: a body without a name never gets here.
            input.Code,
            input.FarmType ?? FarmType.Field,
            input.GlobalGapNumber,
            input.Description,
            input.Address,
            input.ContactPerson,
            input.ContactPhone,
            input.AreaHectares,
            input.Latitude is { } latitude && input.Longitude is { } longitude ? ToPoint(latitude, longitude) : null,
            input.OrderSequence ?? 0,
            input.IsExternal ?? true,
            input.IsActive ?? true);
    }

    /// <summary>X is the longitude, Y the latitude.</summary>
    private static Point ToPoint(double latitude, double longitude) => new(longitude, latitude) { SRID = Farm.Srid };

    private FarmDto ToDto(Farm farm) => new(
        farm.Id,
        farm.Name,
        farm.Code,
        farm.FarmType,
        farm.GlobalGapNumber,
        farm.Description,
        farm.Address,
        farm.ContactPerson,
        farm.ContactPhone,
        farm.AreaHectares,
        farm.Location?.Y,
        farm.Location?.X,
        farm.OrderSequence,
        farm.IsExternal,
        farm.IsActive,
        farm.CreatedOn,
        farms.ETagOf(farm));
}
