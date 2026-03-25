using Chiro.App.Models;
using Chiro.App.Services;
using FluentAssertions;
using NetTopologySuite.Geometries;
using Xunit;

namespace Chiro.Tests;

[Collection("Database collection")]
public class SpatialQueryServiceTests : IClassFixture<PostGisFixture>, IAsyncLifetime
{
    private readonly PostGisFixture _fixture;
    private readonly GeometryFactory _geometryFactory;
    private SpatialQueryService _service = null!;

    public SpatialQueryServiceTests(PostGisFixture fixture)
    {
        _fixture = fixture;
        // EPSG:2154 (Lambert 93) standard for the app
        _geometryFactory = new GeometryFactory(new PrecisionModel(), 2154);
    }

    public async Task InitializeAsync()
    {
        // Cleanup database before each test
        await using var db = _fixture.CreateDbContext();
        db.ZoneSpecies.RemoveRange(db.ZoneSpecies);
        db.TaxonStatuses.RemoveRange(db.TaxonStatuses);
        db.Taxons.RemoveRange(db.Taxons);
        db.EcologicalZones.RemoveRange(db.EcologicalZones);
        await db.SaveChangesAsync();

        _service = new SpatialQueryService(db);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FindZonesNearProject_WithIntersectingZones_ReturnsOrderedResultsAndCorrectDistance()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();
        
        // Center point: (X, Y) in Lambert 93
        var projectCenter = _geometryFactory.CreatePoint(new Coordinate(600000, 6500000));
        
        // Zone 1: exactly covering the project center (distance 0)
        var zone1 = new EcologicalZone
        {
            Code = "Z1",
            Name = "Zone Inside",
            Type = ZoneType.Znieff1,
            Geometry = _geometryFactory.CreatePolygon(new Coordinate[]
            {
                new(590000, 6490000), new(610000, 6490000), 
                new(610000, 6510000), new(590000, 6510000), 
                new(590000, 6490000)
            })
        };

        // Zone 2: ~200m away
        var zone2 = new EcologicalZone
        {
            Code = "Z2",
            Name = "Zone Close",
            Type = ZoneType.Znieff2,
            Geometry = _geometryFactory.CreatePolygon(new Coordinate[]
            {
                new(600200, 6500000), new(600400, 6500000), 
                new(600400, 6500200), new(600200, 6500200), 
                new(600200, 6500000)
            })
        };

        // Zone 3: 10km away (outside 5000m buffer)
        var zone3 = new EcologicalZone
        {
            Code = "Z3",
            Name = "Zone Far",
            Type = ZoneType.Natura2000Sic,
            Geometry = _geometryFactory.CreatePolygon(new Coordinate[]
            {
                new(610000, 6500000), new(611000, 6500000), 
                new(611000, 6501000), new(610000, 6501000), 
                new(610000, 6500000)
            })
        };

        db.EcologicalZones.AddRange(zone1, zone2, zone3);
        await db.SaveChangesAsync();

        var service = new SpatialQueryService(db);

        // Act
        // Searching within 5000 meters
        var results = service.FindZonesNearProject(projectCenter, 5000);

        // Assert
        results.Should().HaveCount(2, "Zone 3 is 10km away and buffer is 5km");
        
        // Zone inside should be first (distance 0)
        results[0].Code.Should().Be("Z1");
        results[0].DistanceMeters.Should().Be(0);
        results[0].IsInside.Should().BeTrue();

        // Zone close should be second
        results[1].Code.Should().Be("Z2");
        results[1].DistanceMeters.Should().BeApproximately(200, precision: 1.0);
        results[1].IsInside.Should().BeFalse();
    }

    [Fact]
    public async Task GetSpeciesForZones_GroupsTaxonsAndStatusesCorrectly()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();

        var zoneA = new EcologicalZone { Code = "ZA", Name = "Zone A", Type = ZoneType.Znieff1, Geometry = _geometryFactory.CreatePoint(new Coordinate(0, 0)) };
        var zoneB = new EcologicalZone { Code = "ZB", Name = "Zone B", Type = ZoneType.Znieff2, Geometry = _geometryFactory.CreatePoint(new Coordinate(1, 1)) };
        db.EcologicalZones.AddRange(zoneA, zoneB);

        var taxon1 = new Taxon
        {
            CdNom = 123, CdRef = 123, ScientificName = "Lynx lynx", VernacularName = "Lynx boréal", Group1Inpn = "Mammifères"
        };
        var taxon2 = new Taxon
        {
            CdNom = 456, CdRef = 456, ScientificName = "Rana temporaria", VernacularName = "Grenouille rousse", Group1Inpn = "Amphibiens"
        };
        db.Taxons.AddRange(taxon1, taxon2);
        
        // Create statuses
        db.TaxonStatuses.AddRange(
            new TaxonStatus { TaxonId = 1, StatusTypeCode = "LRN", StatusCode = "EN", Taxon = taxon1 },
            new TaxonStatus { TaxonId = 2, StatusTypeCode = "PN", StatusCode = "OUI", Taxon = taxon2 }
        );

        // Associate Lynx with Zone A (Determinant) and Zone B
        // Associate Grenouille with Zone A only
        db.ZoneSpecies.AddRange(
            new ZoneSpecies { EcologicalZone = zoneA, Taxon = taxon1, IsDeterminant = true },
            new ZoneSpecies { EcologicalZone = zoneB, Taxon = taxon1, IsDeterminant = false },
            new ZoneSpecies { EcologicalZone = zoneA, Taxon = taxon2, IsDeterminant = false }
        );

        await db.SaveChangesAsync();

        var service = new SpatialQueryService(db);

        // Act
        var results = service.GetSpeciesForZones(new List<int> { zoneA.Id, zoneB.Id });

        // Assert
        results.Should().HaveCount(2);

        // Group 1: Amphibiens (alphabetical sorting by group)
        var gRousse = results[0];
        gRousse.ScientificName.Should().Be("Rana temporaria");
        gRousse.IsDeterminant.Should().BeFalse();
        gRousse.Statuses.Should().ContainSingle(s => s.TypeCode == "PN");
        gRousse.Zones.Should().ContainSingle(z => z.ZoneCode == "ZA");

        // Group 2: Mammifères
        var lynx = results[1];
        lynx.ScientificName.Should().Be("Lynx lynx");
        lynx.IsDeterminant.Should().BeTrue("Because it's determinant in at least one zone");
        lynx.Statuses.Should().ContainSingle(s => s.TypeCode == "LRN" && s.Code == "EN");
        lynx.Zones.Should().HaveCount(2).And.Contain(z => z.ZoneCode == "ZA").And.Contain(z => z.ZoneCode == "ZB");
    }
}
