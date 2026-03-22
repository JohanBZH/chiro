using NetTopologySuite.Geometries;

namespace Chiro.App.Models;

/// <summary>
/// Represents a generic ecological zone (ZNIEFF, Natura 2000, EP, etc.)
/// stored with EPSG:2154 (Lambert 93) projection for France métropolitaine.
/// </summary>
public class EcologicalZone
{
    public int Id { get; set; }

    /// <summary>
    /// Official code from the source dataset.
    /// ZNIEFF: NM_SFFZN (e.g. "210000117"), N2K: sitecode (e.g. "FR5312010"), EP: id_mnhn.
    /// </summary>
    public required string Code { get; set; }

    public required string Name { get; set; }

    public ZoneType Type { get; set; }

    /// <summary>
    /// Surface area in hectares, when available from the source data.
    /// </summary>
    public double? SurfaceHa { get; set; }

    /// <summary>
    /// Human-readable designation label (e.g. "Réserve naturelle nationale", "ZPS").
    /// </summary>
    public string? DesignationLabel { get; set; }

    /// <summary>
    /// The spatial polygon or multi-polygon geometry of the zone.
    /// Used by NetTopologySuite and SpatiaLite for local GIS operations.
    /// </summary>
    public required Geometry Geometry { get; set; }

    /// <summary>
    /// Navigation property: species found in this zone.
    /// </summary>
    public ICollection<ZoneSpecies> ZoneSpecies { get; set; } = new List<ZoneSpecies>();
}
