using Farm360.MasterData.Domain;
using NetTopologySuite.Geometries;

namespace Farm360.MasterData.Tests;

[Trait("service", "master-data")]
public sealed class FarmTests
{
    [Fact]
    public void Text_is_trimmed_and_empty_text_is_not_set()
    {
        var farm = new Farm(new FarmDetails("  Ladang Hijau  ", Code: " FRM-001 ", Address: "   ", ContactPerson: ""), 1);
        farm.Name.ShouldBe("Ladang Hijau");
        farm.Code.ShouldBe("FRM-001");
        farm.Address.ShouldBeNull();
        farm.ContactPerson.ShouldBeNull();
    }

    [Fact]
    public void A_new_farm_is_an_active_external_field_farm_by_default()
    {
        var farm = new Farm(new FarmDetails("Ladang Hijau"), 1);
        farm.FarmType.ShouldBe(FarmType.Field);
        farm.IsActive.ShouldBeTrue();
        farm.IsExternal.ShouldBeTrue();
        farm.OrderSequence.ShouldBe(1);
        farm.Location.ShouldBeNull();
    }

    [Fact]
    public void Updating_replaces_the_details()
    {
        var farm = new Farm(new FarmDetails("Ladang Hijau", Code: "FRM-001", AreaHectares: 12.5m), 1);
        var location = new Point(101.6869, 3.1390) { SRID = Farm.Srid };
        farm.Update(new FarmDetails("Greenhouse A", FarmType: FarmType.Greenhouse, Location: location, IsActive: false));

        farm.Name.ShouldBe("Greenhouse A");
        farm.Code.ShouldBeNull();
        farm.AreaHectares.ShouldBeNull();
        farm.FarmType.ShouldBe(FarmType.Greenhouse);
        farm.Location.ShouldBe(location);
        farm.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void A_location_must_be_in_wgs84()
    {
        var unknownSrid = new Point(101.6869, 3.1390);
        Should.Throw<ArgumentException>(() => new Farm(new FarmDetails("Ladang Hijau", Location: unknownSrid), 1));
    }

    [Fact]
    public void A_farm_moves_to_a_position_from_one_up()
    {
        var farm = new Farm(new FarmDetails("Ladang Hijau"), 3);
        farm.OrderSequence.ShouldBe(3);
        farm.MoveTo(1);
        farm.OrderSequence.ShouldBe(1);
        Should.Throw<ArgumentOutOfRangeException>(() => farm.MoveTo(0));
        Should.Throw<ArgumentOutOfRangeException>(() => new Farm(new FarmDetails("Ladang Biru"), 0));
    }

    [Fact]
    public void Deleting_marks_the_farm_instead_of_removing_it()
    {
        var farm = new Farm(new FarmDetails("Ladang Hijau"), 1);
        var when = new DateTime(2026, 10, 20, 1, 0, 0, DateTimeKind.Utc);
        farm.MarkDeleted(when);
        farm.IsDeleted.ShouldBeTrue();
        farm.DeletedOn.ShouldBe(when);
    }
}
