using Chiro.App.Data;
using Chiro.App.Models;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Chiro.App.Services;

/// <summary>
/// Provides spatial queries against the PostGIS database.
/// All geometries use EPSG:2154 (Lambert 93) — distances are in metres.
/// </summary>
public class SpatialQueryService
{
    private readonly ChiroDbContext _db;

    public SpatialQueryService(ChiroDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Finds all ecological zones that intersect the buffered project geometry.
    /// Computes the distance (metres) from the closest edge of the project to each zone.
    /// </summary>
    /// <param name="projectGeometry">The study perimeter geometry (EPSG:2154).</param>
    /// <param name="radiusMeters">Buffer radius in metres (max 20 000).</param>
    /// <returns>List of zone results ordered by distance.</returns>
    public List<ZoneResult> FindZonesNearProject(Geometry projectGeometry, double radiusMeters)
    {
        // Build a buffer around the project geometry
        var searchArea = projectGeometry.Buffer(radiusMeters);

        // Query zones whose geometry intersects the search area
        var zones = _db.EcologicalZones
            .Where(z => z.Geometry.Intersects(searchArea))
            .Select(z => new
            {
                Zone = z,
                // Distance from project boundary to zone boundary (metres in Lambert 93)
                Distance = z.Geometry.Distance(projectGeometry)
            })
            .OrderBy(x => x.Distance)
            .ToList();

        return zones.Select(x => new ZoneResult
        {
            Id = x.Zone.Id,
            Code = x.Zone.Code,
            Name = x.Zone.Name,
            Type = x.Zone.Type,
            DesignationLabel = x.Zone.DesignationLabel,
            SurfaceHa = x.Zone.SurfaceHa,
            DistanceMeters = Math.Round(x.Distance, 1),
            // Project is inside the zone if distance is 0 or zone contains the geometry
            IsInside = x.Distance < 1.0
        }).ToList();
    }

    /// <summary>
    /// Retrieves species and their conservation statuses for a given list of zone IDs.
    /// Groups by species, aggregating statuses and zone associations.
    /// </summary>
    /// <param name="zoneIds">List of ecological zone IDs from the spatial query.</param>
    /// <returns>List of species with their statuses and zone memberships.</returns>
    public List<SpeciesResult> GetSpeciesForZones(List<int> zoneIds)
    {
        if (zoneIds.Count == 0)
        {
            return new List<SpeciesResult>();
        }

        // Load zone-species associations for the given zones, including taxon + statuses
        var zoneSpecies = _db.ZoneSpecies
            .Where(zs => zoneIds.Contains(zs.EcologicalZoneId))
            .Include(zs => zs.Taxon)
                .ThenInclude(t => t.Statuses)
            .Include(zs => zs.EcologicalZone)
            .ToList();

        // Group by taxon Id to produce one result per species
        var grouped = zoneSpecies
            .GroupBy(zs => zs.TaxonId)
            .Select(g =>
            {
                var taxon = g.First().Taxon;
                return new SpeciesResult
                {
                    CdNom = taxon.CdNom,
                    CdRef = taxon.CdRef,
                    ScientificName = taxon.ScientificName,
                    VernacularName = taxon.VernacularName,
                    Group1Inpn = taxon.Group1Inpn,
                    Group2Inpn = taxon.Group2Inpn,
                    IsDeterminant = g.Any(zs => zs.IsDeterminant == true),
                    Statuses = taxon.Statuses.Select(s => new StatusInfo
                    {
                        TypeCode = s.StatusTypeCode,
                        TypeLabel = s.StatusTypeLabel,
                        Code = s.StatusCode,
                        Label = s.StatusLabel,
                        GeographicScope = s.GeographicScope
                    }).ToList(),
                    Zones = g.Select(zs => new ZoneRef
                    {
                        ZoneId = zs.EcologicalZoneId,
                        ZoneCode = zs.EcologicalZone.Code,
                        ZoneName = zs.EcologicalZone.Name,
                        ZoneType = zs.EcologicalZone.Type
                    }).ToList()
                };
            })
            .OrderBy(s => s.Group1Inpn)
            .ThenBy(s => s.VernacularName ?? s.ScientificName)
            .ToList();

        return grouped;
    }
}

// ──────────────────────────────────────────────
// Result DTOs
// ──────────────────────────────────────────────

/// <summary>
/// Result of a spatial zone search.
/// </summary>
public class ZoneResult
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ZoneType Type { get; set; }
    public string? DesignationLabel { get; set; }
    public double? SurfaceHa { get; set; }
    public double DistanceMeters { get; set; }
    public bool IsInside { get; set; }
}

/// <summary>
/// Species result with all statuses and zone memberships.
/// </summary>
public class SpeciesResult
{
    public int CdNom { get; set; }
    public int CdRef { get; set; }
    public string ScientificName { get; set; } = "";
    public string? VernacularName { get; set; }
    public string? Group1Inpn { get; set; }
    public string? Group2Inpn { get; set; }
    public bool IsDeterminant { get; set; }
    public List<StatusInfo> Statuses { get; set; } = new();
    public List<ZoneRef> Zones { get; set; } = new();
}

/// <summary>
/// A conservation/protection status entry.
/// </summary>
public class StatusInfo
{
    public string TypeCode { get; set; } = "";
    public string? TypeLabel { get; set; }
    public string Code { get; set; } = "";
    public string? Label { get; set; }
    public string? GeographicScope { get; set; }
}

/// <summary>
/// Reference to a zone where a species was found.
/// </summary>
public class ZoneRef
{
    public int ZoneId { get; set; }
    public string ZoneCode { get; set; } = "";
    public string ZoneName { get; set; } = "";
    public ZoneType ZoneType { get; set; }
}
