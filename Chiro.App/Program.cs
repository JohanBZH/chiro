using Chiro.App.Data;
using Chiro.App.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Photino.NET;
using System.Text.Json;
using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Tasks;

namespace Chiro.App;

class Program
{
    private static readonly ConcurrentDictionary<string, object> PendingResponses = new();

    [STAThread]
    static void Main(string[] args)
    {
        // 1. Load Configuration
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        var configuration = builder.Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? "Host=localhost;Port=5435;Database=chiro_db;Username=chiro;Password=password";

        // Path to the datasets directory (TAXREF, BDC, ZNIEFF, NATURA2000, EP)
        var dataDir = configuration["DataDirectory"]
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "database");

        // 2. Initialize Database Code-First
        using (var dbContext = new ChiroDbContext(connectionString))
        {
            dbContext.Database.EnsureCreated();
            Console.WriteLine("Database initialized successfully.");

            // Seed data on first run (if no zones exist yet)
            if (!dbContext.EcologicalZones.Any())
            {
                Console.WriteLine("Empty database detected — starting data import...");
                var importService = new DataImportService(dbContext, dataDir);
                importService.ImportAll();
            }
            else
            {
                var zoneCount = dbContext.EcologicalZones.Count();
                var taxonCount = dbContext.Taxons.Count();
                Console.WriteLine($"Database already seeded: {zoneCount} zones, {taxonCount} taxa.");
            }
        }

        // 3. Setup the UI Window
        var window = new PhotinoWindow()
            .SetTitle("Chiro - Ecological Diagnostics")
            .SetUseOsDefaultSize(false)
            .SetSize(new System.Drawing.Size(1920, 1080))
            .Center()
            .RegisterWebMessageReceivedHandler((object? sender, string message) =>
            {
                var targetWindow = sender as PhotinoWindow;
                string? requestId = null;
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                try
                {
                    Console.WriteLine($"[UI Message Received]: {message}");
                    var doc = JsonDocument.Parse(message);
                    var action = doc.RootElement.GetProperty("action").GetString();

                    // Extract requestId so every response can echo it back
                    if (doc.RootElement.TryGetProperty("requestId", out var reqIdEl))
                    {
                        requestId = reqIdEl.GetString();
                    }

                    using var db = new ChiroDbContext(connectionString);
                    var queryService = new SpatialQueryService(db);

                    switch (action)
                    {
                        case "pickFile":
                            HandlePickFile(targetWindow, options, requestId);
                            break;

                        case "processPerimeter":
                            HandleProcessPerimeter(doc.RootElement, queryService, targetWindow, options, requestId);
                            break;

                        case "findZones":
                            HandleFindZones(doc.RootElement, queryService, targetWindow, options, requestId);
                            break;

                        case "getSpecies":
                            HandleGetSpecies(doc.RootElement, queryService, targetWindow, options, requestId);
                            break;

                        case "getStats":
                            HandleGetStats(db, targetWindow, options, requestId);
                            break;

                        default:
                            SafeSendWebMessage(targetWindow, new { status = "error", message = $"Unknown action: {action}", requestId }, options);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    SafeSendWebMessage(targetWindow, new { status = "error", message = ex.Message, requestId }, options);
                }
            })
            // Target Vite Dev Server for quick iteration
            .Load("http://localhost:5173");

        // 4. Start Local API background listener for Vue to poll results
        StartLocalApi();

        window.WaitForClose();
    }

    /// <summary>
    /// Starts a local HTTP server robustly handling C#->JS IPC via long-polling.
    /// Bypasses native WebKitGTK Photino syntax breaking bugs.
    /// </summary>
    private static void StartLocalApi()
    {
        Task.Run(() =>
        {
            var listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:5174/");
            listener.Start();
            Console.WriteLine("[IPC] Local HTTP server listening on http://127.0.0.1:5174");

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            while (true)
            {
                var ctx = listener.GetContext();
                ctx.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                ctx.Response.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (ctx.Request.HttpMethod == "OPTIONS")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.Close();
                    continue;
                }

                try
                {
                    if (ctx.Request.Url?.AbsolutePath == "/api/poll")
                    {
                        var reqId = ctx.Request.QueryString["requestId"];
                        if (string.IsNullOrEmpty(reqId))
                        {
                            ctx.Response.StatusCode = 400;
                            ctx.Response.Close();
                            continue;
                        }

                        // Wait up to 120 seconds for the response
                        object? result = null;
                        for (int i = 0; i < 1200; i++)
                        {
                            if (PendingResponses.TryRemove(reqId, out result))
                                break;
                            System.Threading.Thread.Sleep(100);
                        }

                        if (result != null)
                        {
                            var resBytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result, options));
                            ctx.Response.ContentType = "application/json";
                            ctx.Response.OutputStream.Write(resBytes, 0, resBytes.Length);
                        }
                        else
                        {
                            ctx.Response.StatusCode = 408; // Timeout
                        }
                    }
                    else
                    {
                        ctx.Response.StatusCode = 404;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[IPC] HTTP Listener Error: " + ex.Message);
                    ctx.Response.StatusCode = 500;
                }
                finally
                {
                    ctx.Response.Close();
                }
            }
        });
    }

    /// <summary>
    /// Safely stores the message for the frontend to fetch via HTTP IPC.
    /// Bypasses Photino's native SendWebMessage which fails on WebKitGTK cross-origin.
    /// </summary>
    private static void SafeSendWebMessage(PhotinoWindow? window, object payload, JsonSerializerOptions options)
    {
        var json = JsonSerializer.Serialize(payload, options);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("requestId", out var reqEl))
        {
            var reqId = reqEl.GetString();
            if (!string.IsNullOrEmpty(reqId))
            {
                PendingResponses[reqId] = payload;
                Console.WriteLine($"[IPC] Response queued for requestId: {reqId}");
            }
        }
    }

    /// <summary>
    /// Opens a native OS file dialog to pick a spatial file (.shp, .gpkg).
    /// Returns the absolute path to the frontend.
    /// </summary>
    private static void HandlePickFile(
        PhotinoWindow? window, JsonSerializerOptions options, string? requestId)
    {
        if (window == null) return;

        // ShowOpenFile returns an array of selected file paths
        var files = window.ShowOpenFile(
            title: "Sélectionner un périmètre d'étude (.shp, .gpkg)",
            defaultPath: null,
            multiSelect: false,
            filters: null);

        if (files != null && files.Length > 0 && !string.IsNullOrEmpty(files[0]))
        {
            var selectedPath = files[0];
            Console.WriteLine($"[PickFile] Selected: {selectedPath}");

            SafeSendWebMessage(window, new { status = "success", action = "pickFile", data = new { filePath = selectedPath }, requestId }, options);
        }
        else
        {
            // User cancelled the dialog
            SafeSendWebMessage(window, new { status = "cancelled", action = "pickFile", requestId }, options);
        }
    }

    /// <summary>
    /// Handles "processPerimeter": reads a shapefile from disk,
    /// extracts / unions its geometries, then runs FindZonesNearProject.
    /// Expected payload: { action: "processPerimeter", data: { filePath: "...", radiusKm: 20 } }
    /// </summary>
    private static void HandleProcessPerimeter(
        JsonElement root, SpatialQueryService queryService,
        PhotinoWindow? window, JsonSerializerOptions options, string? requestId)
    {
        var data = root.GetProperty("data");
        var filePath = data.GetProperty("filePath").GetString()
                       ?? throw new ArgumentException("filePath is required");
        var radiusKm = data.GetProperty("radiusKm").GetDouble();
        var radiusMeters = radiusKm * 1000;

        Console.WriteLine($"[ProcessPerimeter] File: {filePath}, Radius: {radiusKm}km");

        if (!File.Exists(filePath))
        {
            SafeSendWebMessage(window, new { status = "error", message = $"File not found: {filePath}", requestId }, options);
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        Geometry projectGeometry;

        switch (ext)
        {
            case ".shp":
                projectGeometry = ReadShapefile(filePath);
                break;
            case ".gpkg":
                projectGeometry = ReadGeoPackage(filePath);
                break;
            default:
                SafeSendWebMessage(window, new { status = "error", message = $"Unsupported file format: {ext}. Supported: .shp, .gpkg", requestId }, options);
                return;
        }

        Console.WriteLine($"[ProcessPerimeter] Geometry type: {projectGeometry.GeometryType}, SRID: {projectGeometry.SRID}");

        var zones = queryService.FindZonesNearProject(projectGeometry, radiusMeters);

        Console.WriteLine($"[ProcessPerimeter] Found {zones.Count} zones within {radiusKm}km");

        var insideZoneIds = zones.Where(z => z.IsInside).Select(z => z.Id).ToList();
        var species = queryService.GetSpeciesForZones(insideZoneIds);
        Console.WriteLine($"[ProcessPerimeter] Found {species.Count} species in {insideZoneIds.Count} inside zones");

        var geoJsonWriter = new NetTopologySuite.IO.GeoJsonWriter();
        var perimeterGeoJson = geoJsonWriter.Write(projectGeometry);

        var resultPayload = new
        {
            perimeter = perimeterGeoJson,
            zones = zones,
            species = species
        };

        SafeSendWebMessage(window, new { status = "success", action = "processPerimeter", data = resultPayload, requestId }, options);
    }

    /// <summary>
    /// Reads all geometries from a shapefile and returns their union.
    /// Assumes EPSG:2154 (Lambert 93) if no .prj file declares otherwise.
    /// </summary>
    private static Geometry ReadShapefile(string shpPath)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 2154);
        using var reader = new ShapefileDataReader(shpPath, factory);

        Geometry? union = null;

        while (reader.Read())
        {
            var geom = reader.Geometry;
            if (geom == null) continue;
            geom.SRID = 2154;

            union = union == null ? geom : union.Union(geom);
        }

        if (union == null)
        {
            throw new InvalidOperationException("Shapefile contains no valid geometries.");
        }

        return union;
    }

    /// <summary>
    /// Reads all geometries from a GeoPackage file and returns their union.
    /// Queries gpkg_contents to discover the first features table,
    /// then reads geometry blobs via GeoPackageGeoReader.
    /// </summary>
    private static Geometry ReadGeoPackage(string gpkgPath)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 2154);
        var geoReader = new GeoPackageGeoReader(
            factory.CoordinateSequenceFactory, factory.PrecisionModel);

        using var connection = new SqliteConnection($"Data Source={gpkgPath};Mode=ReadOnly");
        connection.Open();

        // Discover the first features table and its geometry column from gpkg_contents
        string tableName;
        string geometryColumn;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT gc.table_name, gc.column_name
                FROM gpkg_geometry_columns gc
                JOIN gpkg_contents c ON gc.table_name = c.table_name
                WHERE c.data_type = 'features'
                LIMIT 1;";

            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException(
                    "GeoPackage contains no feature tables (gpkg_geometry_columns is empty).");
            }
            tableName = reader.GetString(0);
            geometryColumn = reader.GetString(1);
        }

        Console.WriteLine($"[ReadGeoPackage] Table: {tableName}, Column: {geometryColumn}");

        // Read all geometry blobs and union them
        Geometry? union = null;
        using (var cmd = connection.CreateCommand())
        {
            // Table/column names are from gpkg metadata, not user input — safe to interpolate
            cmd.CommandText = $"SELECT \"{geometryColumn}\" FROM \"{tableName}\" WHERE \"{geometryColumn}\" IS NOT NULL;";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var blob = (byte[])reader.GetValue(0);
                var geom = geoReader.Read(blob);
                if (geom == null) continue;

                geom.SRID = 2154;
                union = union == null ? geom : union.Union(geom);
            }
        }

        if (union == null)
        {
            throw new InvalidOperationException(
                $"GeoPackage table '{tableName}' contains no valid geometries.");
        }

        return union;
    }

    /// <summary>
    /// Handles "findZones" action: finds zones intersecting a buffered point.
    /// Expected payload: { action: "findZones", x: 842869, y: 6519584, radius: 5000 }
    /// </summary>
    private static void HandleFindZones(
        JsonElement root, SpatialQueryService queryService,
        PhotinoWindow? window, JsonSerializerOptions options, string? requestId)
    {
        var x = root.GetProperty("x").GetDouble();
        var y = root.GetProperty("y").GetDouble();
        var radius = root.GetProperty("radius").GetDouble();

        var factory = new GeometryFactory(new PrecisionModel(), 2154);
        var projectPoint = factory.CreatePoint(new Coordinate(x, y));

        var zones = queryService.FindZonesNearProject(projectPoint, radius);

        SafeSendWebMessage(window, new { status = "success", action = "findZones", data = zones, requestId }, options);
    }

    /// <summary>
    /// Handles "getSpecies" action: gets species for given zone IDs.
    /// Expected payload: { action: "getSpecies", zoneIds: [1, 2, 3] }
    /// </summary>
    private static void HandleGetSpecies(
        JsonElement root, SpatialQueryService queryService,
        PhotinoWindow? window, JsonSerializerOptions options, string? requestId)
    {
        var zoneIds = root.GetProperty("zoneIds")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .ToList();

        var species = queryService.GetSpeciesForZones(zoneIds);

        SafeSendWebMessage(window, new { status = "success", action = "getSpecies", data = species, requestId }, options);
    }

    /// <summary>
    /// Handles "getStats" action: returns database statistics.
    /// </summary>
    private static void HandleGetStats(
        ChiroDbContext db, PhotinoWindow? window, JsonSerializerOptions options, string? requestId)
    {
        var stats = new
        {
            zones = db.EcologicalZones.Count(),
            taxons = db.Taxons.Count(),
            statuses = db.TaxonStatuses.Count(),
            zoneSpecies = db.ZoneSpecies.Count()
        };

        SafeSendWebMessage(window, new { status = "success", action = "getStats", data = stats, requestId }, options);
    }
}

