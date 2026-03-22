using Chiro.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Chiro.App.Data;

public class ChiroDbContext : DbContext
{
    private readonly string _connectionString;

    public ChiroDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<EcologicalZone> EcologicalZones { get; set; }
    public DbSet<Taxon> Taxons { get; set; }
    public DbSet<TaxonStatus> TaxonStatuses { get; set; }
    public DbSet<ZoneSpecies> ZoneSpecies { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(_connectionString, x => x.UseNetTopologySuite());
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // PostGIS extension
        modelBuilder.HasPostgresExtension("postgis");

        // Taxon: CdNom is a unique business key, not the PK
        modelBuilder.Entity<Taxon>(entity =>
        {
            entity.HasIndex(t => t.CdNom).IsUnique();
        });

        // EcologicalZone: GiST spatial index for PostGIS geometry queries
        modelBuilder.Entity<EcologicalZone>(entity =>
        {
            entity.HasIndex(z => z.Code);
            entity.HasIndex(z => z.Geometry).HasMethod("gist");
        });

        // ZoneSpecies: composite PK (not inferable by convention)
        modelBuilder.Entity<ZoneSpecies>(entity =>
        {
            entity.HasKey(zs => new { zs.EcologicalZoneId, zs.TaxonId });
        });
    }
}
