namespace Chiro.App.Models;

/// <summary>
/// Junction table linking an ecological zone to a taxon.
/// </summary>
public class ZoneSpecies
{
    public int EcologicalZoneId { get; set; }
    public int TaxonId { get; set; }

    /// <summary>
    /// Whether this species is "déterminante" for the zone (ZNIEFF: fg_esp == "D").
    /// </summary>
    public bool? IsDeterminant { get; set; }

    /// <summary>
    /// Taxonomic group label from the source data (e.g. "Oiseaux", "Mammifères").
    /// </summary>
    public string? TaxonomicGroup { get; set; }

    public EcologicalZone EcologicalZone { get; set; } = null!;
    public Taxon Taxon { get; set; } = null!;
}
