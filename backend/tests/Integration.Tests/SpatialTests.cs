using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Farm360.Integration.Tests;

/// <summary>Spatial values (NetTopologySuite) are stored as geography with SRID 4326 on every provider (docs/adr/0024).</summary>
public sealed class SpatialTests(Databases databases)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Kuala Lumpur, and a point about 550 m east of it. X is longitude, Y is latitude.
    private static Point KualaLumpur => new(101.6869, 3.1390) { SRID = 4326 };

    private static Point NearKualaLumpur => new(101.6919, 3.1390) { SRID = 4326 };

    [Theory]
    [MemberData(nameof(Databases.Providers), MemberType = typeof(Databases))]
    public async Task A_point_round_trips_as_geography(string provider)
    {
        await using var host = new TestModuleHost(provider, databases.NewDatabase(provider));
        await host.CreateDatabaseAsync();
        Guid id;
        var (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            db.Model.FindEntityType(typeof(TestPlace))!.FindProperty(nameof(TestPlace.Location))!.GetColumnType().ShouldStartWith("geography");
            var place = new TestPlace("farm", KualaLumpur);
            db.Places.Add(place);
            await db.SaveChangesAsync(Ct);
            id = place.Id;
        }

        (scope, db) = host.NewContext();
        await using (scope)
        await using (db)
        {
            var location = (await db.Places.SingleAsync(p => p.Id == id, Ct)).Location;
            location.X.ShouldBe(101.6869, 1e-9);
            location.Y.ShouldBe(3.1390, 1e-9);
            location.SRID.ShouldBe(4326);

            // Geography measures in metres (geometry would measure in degrees, and both points would be within 100 "units").
            var near = NearKualaLumpur;
            (await db.Places.CountAsync(p => p.Location.IsWithinDistance(near, 1000), Ct)).ShouldBe(1);
            (await db.Places.CountAsync(p => p.Location.IsWithinDistance(near, 100), Ct)).ShouldBe(0);
        }
    }
}
