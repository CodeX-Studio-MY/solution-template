using Farm360.Core.Data;
using NetTopologySuite.Geometries;

namespace Farm360.MasterData.Domain;

/// <summary>Everything about a farm that a user edits. The farm trims the text and keeps empty text as "not set".</summary>
public sealed record FarmDetails(
    string Name,
    string? Code = null,
    FarmType FarmType = FarmType.Field,
    string? GlobalGapNumber = null,
    string? Description = null,
    string? Address = null,
    string? ContactPerson = null,
    string? ContactPhone = null,
    decimal? AreaHectares = null,
    Point? Location = null,
    bool IsExternal = true,
    bool IsActive = true);

/// <summary>A farm of the tenant: where it is, what kind it is and who to contact. All changes go through <see cref="Update"/>.</summary>
public sealed class Farm : BaseEntity, IAggregateRoot, ISoftDeletable
{
    public const int MaxNameLength = 200;
    public const int MaxCodeLength = 50;
    public const int MaxGlobalGapNumberLength = 50;
    public const int MaxDescriptionLength = 1000;
    public const int MaxAddressLength = 500;
    public const int MaxContactPersonLength = 200;
    public const int MaxContactPhoneLength = 50;

    /// <summary>WGS 84: latitude and longitude in degrees.</summary>
    public const int Srid = 4326;

    private Farm()
    {
    }

    /// <summary>A new farm at <paramref name="position"/> in the tenant's list (the service places it at the end).</summary>
    public Farm(FarmDetails details, int position)
    {
        Apply(details);
        MoveTo(position);
    }

    public string Name { get; private set; } = "";

    /// <summary>The tenant's own short code; unique among the farms that are not deleted, when set.</summary>
    public string? Code { get; private set; }

    public FarmType FarmType { get; private set; }

    /// <summary>The GLOBALG.A.P. number (GGN) of a certified farm.</summary>
    public string? GlobalGapNumber { get; private set; }

    public string? Description { get; private set; }

    public string? Address { get; private set; }

    public string? ContactPerson { get; private set; }

    public string? ContactPhone { get; private set; }

    public decimal? AreaHectares { get; private set; }

    /// <summary>Where the farm is (SRID 4326, X is the longitude).</summary>
    public Point? Location { get; private set; }

    /// <summary>Where the farm appears in lists: 1 to N with no gaps. The service keeps the other farms in step when one moves.</summary>
    public int OrderSequence { get; private set; }

    /// <summary>The farm belongs to someone else (for example a contract grower), not to the tenant.</summary>
    public bool IsExternal { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime? DeletedOn { get; private set; }

    public void Update(FarmDetails details) => Apply(details);

    /// <summary>The code as the farm stores it (trimmed; empty is not set), so a uniqueness check compares like with like.</summary>
    public static string? NormalizeCode(string? code) => OrNull(code);

    /// <summary>Puts the farm at <paramref name="position"/>. Shifting the farms in between is the service's job.</summary>
    public void MoveTo(int position)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(position, 1);
        OrderSequence = position;
    }

    public void MarkDeleted(DateTime utcNow)
    {
        IsDeleted = true;
        DeletedOn = utcNow;
    }

    private void Apply(FarmDetails details)
    {
        if (details.Location is { } location && location.SRID != Srid)
            throw new ArgumentException($"A farm location must use SRID {Srid} (WGS 84).", nameof(details));

        Name = details.Name.Trim();
        Code = NormalizeCode(details.Code);
        FarmType = details.FarmType;
        GlobalGapNumber = OrNull(details.GlobalGapNumber);
        Description = OrNull(details.Description);
        Address = OrNull(details.Address);
        ContactPerson = OrNull(details.ContactPerson);
        ContactPhone = OrNull(details.ContactPhone);
        AreaHectares = details.AreaHectares;
        Location = details.Location;
        IsExternal = details.IsExternal;
        IsActive = details.IsActive;
    }

    private static string? OrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
