using Chiro.App.Data;
using Chiro.App.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Photino.NET;
using System.Text.Json;
using System;
using System.IO;
using System.Linq;

namespace Chiro.App;

class Program
{
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
            .SetSize(new System.Drawing.Size(1280, 720))
            .Center()
            .RegisterWebMessageReceivedHandler((object? sender, string message) =>
            {
                var targetWindow = sender as PhotinoWindow;

                try
                {
                    Console.WriteLine($"[UI Message Received]: {message}");
                    var doc = JsonDocument.Parse(message);
                    var action = doc.RootElement.GetProperty("action").GetString();

                    using var db = new ChiroDbContext(connectionString);
                    var queryService = new SpatialQueryService(db);
                    var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

                    switch (action)
                    {
                        case "findZones":
                            HandleFindZones(doc.RootElement, queryService, targetWindow, options);
                            break;

                        case "getSpecies":
                            HandleGetSpecies(doc.RootElement, queryService, targetWindow, options);
                            break;

                        case "getStats":
                            HandleGetStats(db, targetWindow, options);
                            break;

                        default:
                            var response = JsonSerializer.Serialize(
                                new { status = "error", message = $"Unknown action: {action}" }, options);
                            targetWindow?.SendWebMessage(response);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing message: {ex.Message}");
                    targetWindow?.SendWebMessage(
                        JsonSerializer.Serialize(new { status = "error", message = ex.Message }));
                }
            })
            // Target Vite Dev Server for quick iteration
            .Load("http://localhost:5173");

        window.WaitForClose();
    }

    /// <summary>
    /// Handles "findZones" action: finds zones intersecting a buffered point or geometry.
    /// Expected payload: { action: "findZones", x: 842869, y: 6519584, radius: 5000 }
    /// Coordinates are in Lambert 93 (EPSG:2154).
    /// </summary>
    private static void HandleFindZones(
        JsonElement root, SpatialQueryService queryService,
        PhotinoWindow? window, JsonSerializerOptions options)
    {
        var x = root.GetProperty("x").GetDouble();
        var y = root.GetProperty("y").GetDouble();
        var radius = root.GetProperty("radius").GetDouble();

        // Create a point geometry in Lambert 93
        var factory = new GeometryFactory(new PrecisionModel(), 2154);
        var projectPoint = factory.CreatePoint(new Coordinate(x, y));

        var zones = queryService.FindZonesNearProject(projectPoint, radius);

        var response = JsonSerializer.Serialize(
            new { status = "success", action = "findZones", data = zones }, options);
        window?.SendWebMessage(response);
    }

    /// <summary>
    /// Handles "getSpecies" action: gets species for given zone IDs.
    /// Expected payload: { action: "getSpecies", zoneIds: [1, 2, 3] }
    /// </summary>
    private static void HandleGetSpecies(
        JsonElement root, SpatialQueryService queryService,
        PhotinoWindow? window, JsonSerializerOptions options)
    {
        var zoneIds = root.GetProperty("zoneIds")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .ToList();

        var species = queryService.GetSpeciesForZones(zoneIds);

        var response = JsonSerializer.Serialize(
            new { status = "success", action = "getSpecies", data = species }, options);
        window?.SendWebMessage(response);
    }

    /// <summary>
    /// Handles "getStats" action: returns database statistics.
    /// </summary>
    private static void HandleGetStats(
        ChiroDbContext db, PhotinoWindow? window, JsonSerializerOptions options)
    {
        var stats = new
        {
            zones = db.EcologicalZones.Count(),
            taxons = db.Taxons.Count(),
            statuses = db.TaxonStatuses.Count(),
            zoneSpecies = db.ZoneSpecies.Count()
        };

        var response = JsonSerializer.Serialize(
            new { status = "success", action = "getStats", data = stats }, options);
        window?.SendWebMessage(response);
    }
}
