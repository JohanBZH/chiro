namespace Chiro.App.Models;

/// <summary>
/// A conservation or protection status for a taxon, sourced from the BDC.
/// Each taxon can have multiple statuses (e.g. global red list, national red list, etc.)
/// </summary>
public class TaxonStatus
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to Taxon.
    /// </summary>
    public int TaxonId { get; set; }

    /// <summary>
    /// Status type code from BDC (e.g. "LRM", "LRE", "LRN", "LRR", "DH", "PN", "DO").
    /// </summary>
    public required string StatusTypeCode { get; set; }

    /// <summary>
    /// Status value code (e.g. "CR", "EN", "VU", "NT", "LC", "DD", "NE").
    /// </summary>
    public required string StatusCode { get; set; }

    /// <summary>
    /// Human-readable label for the status type (LB_TYPE_STATUT from BDC).
    /// </summary>
    public string? StatusTypeLabel { get; set; }

    /// <summary>
    /// Human-readable label for the status value (LABEL_STATUT from BDC).
    /// </summary>
    public string? StatusLabel { get; set; }

    /// <summary>
    /// Geographic scope (CD_SIG from BDC, e.g. "WORLD", "EUR", "FRF").
    /// </summary>
    public string? GeographicScope { get; set; }

    public Taxon Taxon { get; set; } = null!;
}
