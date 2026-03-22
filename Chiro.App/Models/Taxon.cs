namespace Chiro.App.Models;

/// <summary>
/// A species (or sub-species) from the TAXREF reference database.
/// Only species referenced by at least one zone are imported.
/// </summary>
public class Taxon
{
    public int Id { get; set; }

    /// <summary>
    /// TAXREF CD_NOM: unique identifier for this taxon name entry.
    /// </summary>
    public int CdNom { get; set; }

    /// <summary>
    /// TAXREF CD_REF: identifier for the accepted/reference taxon.
    /// Multiple CD_NOM may point to the same CD_REF (synonyms).
    /// </summary>
    public int CdRef { get; set; }

    /// <summary>
    /// Scientific name (LB_NOM from TAXREF).
    /// </summary>
    public required string ScientificName { get; set; }

    /// <summary>
    /// French vernacular name (NOM_VERN from TAXREF, or from TAXVERNv18).
    /// </summary>
    public string? VernacularName { get; set; }

    /// <summary>
    /// Taxonomic rank code (RANG from TAXREF): ES = species, SSES = subspecies, etc.
    /// </summary>
    public string? Rank { get; set; }

    /// <summary>
    /// High-level INPN group (e.g. "Oiseaux", "Mammifères", "Insectes").
    /// </summary>
    public string? Group1Inpn { get; set; }

    /// <summary>
    /// Detailed INPN group (e.g. "Chiroptères", "Rapaces").
    /// </summary>
    public string? Group2Inpn { get; set; }

    public ICollection<TaxonStatus> Statuses { get; set; } = new List<TaxonStatus>();
    public ICollection<ZoneSpecies> ZoneSpecies { get; set; } = new List<ZoneSpecies>();
}
