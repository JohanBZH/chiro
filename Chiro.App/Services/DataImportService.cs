using System.Globalization;
using System.Text;
using Chiro.App.Data;
using Chiro.App.Models;
using CsvHelper;
using CsvHelper.Configuration;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Chiro.App.Services;

/// <summary>
/// Imports local Patrinat datasets (ZNIEFF, NATURA 2000, EP, TAXREF, BDC)
/// into the PostGIS database. Designed to run once at first launch.
/// All shapefiles from France métropolitaine are assumed EPSG:2154 (Lambert 93).
/// </summary>
public class DataImportService
{
    private readonly ChiroDbContext _db;
    private readonly string _dataDir;
    private const int BatchSize = 1000;
    private const int Srid = 2154; // Lambert 93

    public DataImportService(ChiroDbContext db, string dataDir)
    {
        _db = db;
        _dataDir = dataDir;
    }

    /// <summary>
    /// Temporary structure to hold a parsed zone-species association before
    /// Taxon IDs are known. CdNom is resolved to TaxonId after TAXREF import.
    /// </summary>
    private record PendingZoneSpecies(int ZoneId, int CdNom, bool? IsDeterminant, string? TaxonomicGroup);

    /// <summary>
    /// Runs the full import pipeline:
    /// 1. Import zone geometries (ZNIEFF, N2K, EP)
    /// 2. Parse zone-species mappings → collect referenced CD_NOMs
    /// 3. Import filtered TAXREF → build CdNom→TaxonId lookup
    /// 4. Import filtered BDC statuses using the lookup
    /// 5. Insert ZoneSpecies records using the lookup
    /// </summary>
    public void ImportAll()
    {
        Console.WriteLine("[Import] Starting full data import...");

        // Phase 1: Import zone geometries
        var znieffCodeToId = ImportZnieffZones();
        var n2kCodeToId = ImportNatura2000Zones();
        var epCodeToId = ImportEspacesProteges();

        // Phase 2: Parse species-zone mappings, collect all referenced CD_NOMs
        var pending = new List<PendingZoneSpecies>();
        var referencedCdNoms = new HashSet<int>();

        ParseZnieffSpecies(znieffCodeToId, pending, referencedCdNoms);
        ParseNatura2000Species(n2kCodeToId, pending, referencedCdNoms);
        ParseEpSpecies(epCodeToId, pending, referencedCdNoms);

        Console.WriteLine($"[Import] Total referenced CD_NOMs: {referencedCdNoms.Count}");

        // Phase 3: Import TAXREF (filtered) → returns CdNom → TaxonId mapping
        var cdNomToTaxonId = ImportTaxref(referencedCdNoms);

        // Phase 4: Import BDC statuses (filtered)
        ImportBdcStatuses(cdNomToTaxonId);

        // Phase 5: Insert ZoneSpecies using the CdNom→TaxonId lookup
        InsertZoneSpecies(pending, cdNomToTaxonId);

        Console.WriteLine("[Import] Full data import complete.");
    }

    // ──────────────────────────────────────────────
    // ZNIEFF Import
    // ──────────────────────────────────────────────

    private Dictionary<string, int> ImportZnieffZones()
    {
        var codeToId = new Dictionary<string, int>();

        ImportZnieffShapefile(
            Path.Combine(_dataDir, "ZNIEFF", "SIG_ZNIEFF", "ZNIEFF1_G2.shp"),
            ZoneType.Znieff1, codeToId);

        ImportZnieffShapefile(
            Path.Combine(_dataDir, "ZNIEFF", "SIG_ZNIEFF", "ZNIEFF2_G2.shp"),
            ZoneType.Znieff2, codeToId);

        Console.WriteLine($"[Import] ZNIEFF zones imported: {codeToId.Count}");
        return codeToId;
    }

    private void ImportZnieffShapefile(string shpPath, ZoneType zoneType, Dictionary<string, int> codeToId)
    {
        var factory = new GeometryFactory(new PrecisionModel(), Srid);
        using var reader = new ShapefileDataReader(shpPath, factory);

        var batch = new List<EcologicalZone>();

        while (reader.Read())
        {
            var geometry = reader.Geometry;
            if (geometry == null) continue;
            geometry.SRID = Srid;

            var idMnhn = reader.GetString(reader.GetOrdinal("ID_MNHN"))?.Trim();
            var nom = reader.GetString(reader.GetOrdinal("NOM"))?.Trim();
            if (string.IsNullOrEmpty(idMnhn)) continue;

            double? surface = null;
            var surfOrdinal = reader.GetOrdinal("SUPERFICIE");
            if (!reader.IsDBNull(surfOrdinal))
                surface = reader.GetDouble(surfOrdinal);

            batch.Add(new EcologicalZone
            {
                Code = idMnhn,
                Name = nom ?? idMnhn,
                Type = zoneType,
                SurfaceHa = surface,
                DesignationLabel = zoneType == ZoneType.Znieff1 ? "ZNIEFF Type I" : "ZNIEFF Type II",
                Geometry = geometry
            });

            if (batch.Count >= BatchSize)
                FlushZoneBatch(batch, codeToId);
        }

        if (batch.Count > 0)
            FlushZoneBatch(batch, codeToId);
    }

    // ──────────────────────────────────────────────
    // NATURA 2000 Import
    // ──────────────────────────────────────────────

    private Dictionary<string, int> ImportNatura2000Zones()
    {
        var codeToId = new Dictionary<string, int>();
        var shpPath = Path.Combine(_dataDir, "NATURA2000", "NATURA_BDD_122024", "SIG_NATURA", "natura_sig.shp");
        var factory = new GeometryFactory(new PrecisionModel(), Srid);

        using var reader = new ShapefileDataReader(shpPath, factory);
        var batch = new List<EcologicalZone>();

        while (reader.Read())
        {
            var geometry = reader.Geometry;
            if (geometry == null) continue;
            geometry.SRID = Srid;

            var cdSig = reader.GetString(reader.GetOrdinal("cd_sig"))?.Trim() ?? "";
            var typeEspac = reader.GetString(reader.GetOrdinal("type_espac"))?.Trim() ?? "";

            // Extract sitecode by removing the 4-char prefix (e.g. "I098")
            var sitecode = cdSig.Length > 4 ? cdSig[4..] : cdSig;
            if (string.IsNullOrEmpty(sitecode)) continue;

            var zoneType = typeEspac.ToUpperInvariant() switch
            {
                "ZPS" => ZoneType.Natura2000Zps,
                "ZSC" => ZoneType.Natura2000Zsc,
                "SIC" => ZoneType.Natura2000Sic,
                _ => ZoneType.Natura2000Zsc
            };

            batch.Add(new EcologicalZone
            {
                Code = sitecode,
                Name = sitecode,
                Type = zoneType,
                DesignationLabel = typeEspac,
                Geometry = geometry
            });

            if (batch.Count >= BatchSize)
                FlushZoneBatch(batch, codeToId);
        }

        if (batch.Count > 0)
            FlushZoneBatch(batch, codeToId);

        Console.WriteLine($"[Import] Natura 2000 zones imported: {codeToId.Count}");
        return codeToId;
    }

    // ──────────────────────────────────────────────
    // Espaces Protégés Import
    // ──────────────────────────────────────────────

    private Dictionary<string, int> ImportEspacesProteges()
    {
        var codeToId = new Dictionary<string, int>();
        var epDir = Path.Combine(_dataDir, "EP", "EP_BDD_012026", "ep_202601");

        var siteNames = LoadCsvLookup(Path.Combine(epDir, "ep_site.csv"), "id_mnhn", "nom");
        var designations = LoadCsvLookup(Path.Combine(epDir, "liste_designations.csv"), "id_designation", "lb_designation");
        var siteDesigIds = LoadCsvLookup(Path.Combine(epDir, "ep_site.csv"), "id_mnhn", "id_designation");

        var shpPath = Path.Combine(epDir, "sig", "sig_metrop.shp");
        var factory = new GeometryFactory(new PrecisionModel(), Srid);

        using var reader = new ShapefileDataReader(shpPath, factory);
        var batch = new List<EcologicalZone>();

        while (reader.Read())
        {
            var geometry = reader.Geometry;
            if (geometry == null) continue;
            geometry.SRID = Srid;

            var idMnhn = reader.GetString(reader.GetOrdinal("id_mnhn"))?.Trim() ?? "";
            if (string.IsNullOrEmpty(idMnhn)) continue;

            double? supHa = null;
            var supOrdinal = reader.GetOrdinal("sup_ha");
            if (!reader.IsDBNull(supOrdinal))
                supHa = reader.GetDouble(supOrdinal);

            siteNames.TryGetValue(idMnhn, out var siteName);
            string? designationLabel = null;
            if (siteDesigIds.TryGetValue(idMnhn, out var desigId) &&
                designations.TryGetValue(desigId, out var desigLabel))
                designationLabel = desigLabel;

            batch.Add(new EcologicalZone
            {
                Code = idMnhn,
                Name = siteName ?? idMnhn,
                Type = ZoneType.EspaceProtege,
                SurfaceHa = supHa,
                DesignationLabel = designationLabel,
                Geometry = geometry
            });

            if (batch.Count >= BatchSize)
                FlushZoneBatch(batch, codeToId);
        }

        if (batch.Count > 0)
            FlushZoneBatch(batch, codeToId);

        Console.WriteLine($"[Import] Espaces Protégés zones imported: {codeToId.Count}");
        return codeToId;
    }

    // ──────────────────────────────────────────────
    // Zone-Species Parsing (no DB writes yet)
    // ──────────────────────────────────────────────

    private void ParseZnieffSpecies(
        Dictionary<string, int> zoneCodeToId,
        List<PendingZoneSpecies> pending,
        HashSet<int> referencedCdNoms)
    {
        var csvPath = Path.Combine(_dataDir, "ZNIEFF", "REF_ESPECE.csv");
        using var streamReader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, SemicolonConfig());

        csv.Read();
        csv.ReadHeader();

        var seen = new HashSet<(int zoneId, int cdNom)>();
        int count = 0;

        while (csv.Read())
        {
            var nmSffzn = csv.GetField("nm_sffzn")?.Trim();
            var cdNomStr = csv.GetField("cd_nom")?.Trim();
            var fgEsp = csv.GetField("fg_esp")?.Trim();
            var groupeTaxo = csv.GetField("groupe_taxo")?.Trim();

            if (string.IsNullOrEmpty(nmSffzn) || string.IsNullOrEmpty(cdNomStr)) continue;
            if (!int.TryParse(cdNomStr, out var cdNom)) continue;
            if (!zoneCodeToId.TryGetValue(nmSffzn, out var zoneId)) continue;
            if (!seen.Add((zoneId, cdNom))) continue;

            referencedCdNoms.Add(cdNom);
            pending.Add(new PendingZoneSpecies(
                zoneId, cdNom,
                string.Equals(fgEsp, "D", StringComparison.OrdinalIgnoreCase),
                groupeTaxo));
            count++;
        }

        Console.WriteLine($"[Import] ZNIEFF species associations parsed: {count}");
    }

    private void ParseNatura2000Species(
        Dictionary<string, int> zoneCodeToId,
        List<PendingZoneSpecies> pending,
        HashSet<int> referencedCdNoms)
    {
        var csvPath = Path.Combine(_dataDir, "NATURA2000", "NATURA_BDD_122024", "species.csv.csv");
        using var streamReader = new StreamReader(csvPath, Encoding.GetEncoding("iso-8859-1"));
        using var csv = new CsvReader(streamReader, SemicolonConfig());

        csv.Read();
        csv.ReadHeader();

        var seen = new HashSet<(int zoneId, int cdNom)>();
        int count = 0;

        while (csv.Read())
        {
            var sitecode = csv.GetField("sitecode")?.Trim();
            var cdNomStr = csv.GetField("cd_nom")?.Trim();
            var taxgroup = csv.GetField("taxgroup")?.Trim();

            if (string.IsNullOrEmpty(sitecode) || string.IsNullOrEmpty(cdNomStr)) continue;
            if (!int.TryParse(cdNomStr, out var cdNom)) continue;
            if (!zoneCodeToId.TryGetValue(sitecode, out var zoneId)) continue;
            if (!seen.Add((zoneId, cdNom))) continue;

            referencedCdNoms.Add(cdNom);
            pending.Add(new PendingZoneSpecies(zoneId, cdNom, null, MapN2kTaxGroup(taxgroup)));
            count++;
        }

        Console.WriteLine($"[Import] Natura 2000 species associations parsed: {count}");
    }

    private void ParseEpSpecies(
        Dictionary<string, int> zoneCodeToId,
        List<PendingZoneSpecies> pending,
        HashSet<int> referencedCdNoms)
    {
        var csvPath = Path.Combine(_dataDir, "EP", "EP_BDD_012026", "ep_202601", "ep_espece.csv");
        using var streamReader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, SemicolonConfig());

        csv.Read();
        csv.ReadHeader();

        var seen = new HashSet<(int zoneId, int cdNom)>();
        int count = 0;

        while (csv.Read())
        {
            var idMnhn = csv.GetField("id_mnhn")?.Trim();
            var cdNomRaw = csv.GetField("cd_nom")?.Trim();

            if (string.IsNullOrEmpty(idMnhn) || string.IsNullOrEmpty(cdNomRaw)) continue;

            // Handle comma-decimal format (e.g. "78141,0")
            cdNomRaw = cdNomRaw.Replace(",", ".");
            if (!double.TryParse(cdNomRaw, CultureInfo.InvariantCulture, out var cdNomDouble)) continue;
            var cdNom = (int)cdNomDouble;

            if (!zoneCodeToId.TryGetValue(idMnhn, out var zoneId)) continue;
            if (!seen.Add((zoneId, cdNom))) continue;

            referencedCdNoms.Add(cdNom);
            pending.Add(new PendingZoneSpecies(zoneId, cdNom, null, null));
            count++;
        }

        Console.WriteLine($"[Import] EP species associations parsed: {count}");
    }

    // ──────────────────────────────────────────────
    // TAXREF Import (filtered)
    // ──────────────────────────────────────────────

    /// <summary>
    /// Imports TAXREF species filtered to the referenced CD_NOMs.
    /// Returns a CdNom → Taxon.Id lookup for use in subsequent FK resolution.
    /// </summary>
    private Dictionary<int, int> ImportTaxref(HashSet<int> requiredCdNoms)
    {
        var taxrefPath = Path.Combine(_dataDir, "TAXREF_v18_2025", "TAXREFv18.txt");
        var cdNomToTaxonId = new Dictionary<int, int>();

        var vernacularNames = LoadVernacularNames(requiredCdNoms);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var streamReader = new StreamReader(taxrefPath, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, config);

        csv.Read();
        csv.ReadHeader();

        var batch = new List<Taxon>();

        while (csv.Read())
        {
            var cdNomStr = csv.GetField("CD_NOM")?.Trim();
            if (string.IsNullOrEmpty(cdNomStr) || !int.TryParse(cdNomStr, out var cdNom)) continue;
            if (!requiredCdNoms.Contains(cdNom)) continue;

            var cdRefStr = csv.GetField("CD_REF")?.Trim();
            int.TryParse(cdRefStr, out var cdRef);

            var lbNom = csv.GetField("LB_NOM")?.Trim();
            var nomVern = csv.GetField("NOM_VERN")?.Trim();
            var rang = csv.GetField("RANG")?.Trim();
            var group1 = csv.GetField("GROUP1_INPN")?.Trim();
            var group2 = csv.GetField("GROUP2_INPN")?.Trim();

            if (vernacularNames.TryGetValue(cdNom, out var vernName) && !string.IsNullOrEmpty(vernName))
                nomVern = vernName;

            batch.Add(new Taxon
            {
                CdNom = cdNom,
                CdRef = cdRef,
                ScientificName = lbNom ?? $"Unknown ({cdNom})",
                VernacularName = string.IsNullOrEmpty(nomVern) ? null : nomVern,
                Rank = rang,
                Group1Inpn = group1,
                Group2Inpn = group2
            });

            if (batch.Count >= BatchSize)
            {
                _db.Taxons.AddRange(batch);
                _db.SaveChanges();
                // Record the generated Ids
                foreach (var t in batch)
                    cdNomToTaxonId[t.CdNom] = t.Id;
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.Taxons.AddRange(batch);
            _db.SaveChanges();
            foreach (var t in batch)
                cdNomToTaxonId[t.CdNom] = t.Id;
            batch.Clear();
        }

        Console.WriteLine($"[Import] TAXREF species imported: {cdNomToTaxonId.Count} (of {requiredCdNoms.Count} referenced)");
        return cdNomToTaxonId;
    }

    private Dictionary<int, string> LoadVernacularNames(HashSet<int> requiredCdNoms)
    {
        var result = new Dictionary<int, string>();
        var path = Path.Combine(_dataDir, "TAXREF_v18_2025", "TAXVERNv18.txt");

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var streamReader = new StreamReader(path, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, config);

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var cdNomStr = csv.GetField("CD_NOM")?.Trim();
            var langue = csv.GetField("LANGUE")?.Trim();
            var lbVern = csv.GetField("LB_VERN")?.Trim();

            if (string.IsNullOrEmpty(cdNomStr) || !int.TryParse(cdNomStr, out var cdNom)) continue;
            if (!requiredCdNoms.Contains(cdNom)) continue;

            if (string.Equals(langue, "Français", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(lbVern))
                result.TryAdd(cdNom, lbVern);
        }

        Console.WriteLine($"[Import] Vernacular names loaded: {result.Count}");
        return result;
    }

    // ──────────────────────────────────────────────
    // BDC Import (filtered)
    // ──────────────────────────────────────────────

    private void ImportBdcStatuses(Dictionary<int, int> cdNomToTaxonId)
    {
        var csvPath = Path.Combine(_dataDir, "BDC", "BDC_18", "bdc_18_01.csv");

        var relevantTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "LRM", "LRE", "LRN", "LRR", "DH", "DO", "PN", "PR", "PD", "DHFF"
        };

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ",",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };

        using var streamReader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, config);

        csv.Read();
        csv.ReadHeader();

        var batch = new List<TaxonStatus>();
        int count = 0;

        while (csv.Read())
        {
            var cdNomStr = csv.GetField("CD_NOM")?.Trim();
            if (string.IsNullOrEmpty(cdNomStr) || !int.TryParse(cdNomStr, out var cdNom)) continue;
            if (!cdNomToTaxonId.TryGetValue(cdNom, out var taxonId)) continue;

            var typeCode = csv.GetField("CD_TYPE_STATUT")?.Trim();
            if (string.IsNullOrEmpty(typeCode) || !relevantTypes.Contains(typeCode)) continue;

            var statusCode = csv.GetField("CODE_STATUT")?.Trim();
            if (string.IsNullOrEmpty(statusCode)) continue;

            batch.Add(new TaxonStatus
            {
                TaxonId = taxonId,
                StatusTypeCode = typeCode,
                StatusCode = statusCode,
                StatusTypeLabel = csv.GetField("LB_TYPE_STATUT")?.Trim(),
                StatusLabel = csv.GetField("LABEL_STATUT")?.Trim(),
                GeographicScope = csv.GetField("CD_SIG")?.Trim()
            });
            count++;

            if (batch.Count >= BatchSize)
            {
                _db.TaxonStatuses.AddRange(batch);
                _db.SaveChanges();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.TaxonStatuses.AddRange(batch);
            _db.SaveChanges();
        }

        Console.WriteLine($"[Import] BDC statuses imported: {count}");
    }

    // ──────────────────────────────────────────────
    // ZoneSpecies Insert
    // ──────────────────────────────────────────────

    private void InsertZoneSpecies(List<PendingZoneSpecies> pending, Dictionary<int, int> cdNomToTaxonId)
    {
        var batch = new List<ZoneSpecies>();
        int count = 0;

        foreach (var p in pending)
        {
            // Resolve CdNom → TaxonId. Skip if taxon wasn't imported.
            if (!cdNomToTaxonId.TryGetValue(p.CdNom, out var taxonId)) continue;

            batch.Add(new ZoneSpecies
            {
                EcologicalZoneId = p.ZoneId,
                TaxonId = taxonId,
                IsDeterminant = p.IsDeterminant,
                TaxonomicGroup = p.TaxonomicGroup
            });
            count++;

            if (batch.Count >= BatchSize)
            {
                _db.ZoneSpecies.AddRange(batch);
                _db.SaveChanges();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            _db.ZoneSpecies.AddRange(batch);
            _db.SaveChanges();
        }

        Console.WriteLine($"[Import] ZoneSpecies associations inserted: {count}");
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private void FlushZoneBatch(List<EcologicalZone> batch, Dictionary<string, int> codeToId)
    {
        _db.EcologicalZones.AddRange(batch);
        _db.SaveChanges();
        foreach (var zone in batch)
            codeToId.TryAdd(zone.Code, zone.Id);
        batch.Clear();
    }

    /// <summary>
    /// Loads a simple key→value lookup from a semicolon CSV (first match wins).
    /// </summary>
    private static Dictionary<string, string> LoadCsvLookup(string csvPath, string keyField, string valueField)
    {
        var result = new Dictionary<string, string>();
        using var streamReader = new StreamReader(csvPath, Encoding.UTF8);
        using var csv = new CsvReader(streamReader, SemicolonConfig());

        csv.Read();
        csv.ReadHeader();

        while (csv.Read())
        {
            var key = csv.GetField(keyField)?.Trim();
            var value = csv.GetField(valueField)?.Trim();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                result.TryAdd(key, value);
        }

        return result;
    }

    private static string? MapN2kTaxGroup(string? code)
    {
        return code?.ToUpperInvariant() switch
        {
            "B" => "Oiseaux",
            "M" => "Mammifères",
            "A" => "Amphibiens",
            "R" => "Reptiles",
            "F" => "Poissons",
            "I" => "Invertébrés",
            "P" => "Plantes",
            _ => code
        };
    }

    private static CsvConfiguration SemicolonConfig()
    {
        return new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null
        };
    }
}
