using Chiro.App.Data;
using Chiro.App.Models;
using Chiro.App.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Chiro.Tests;

[Collection("Database collection")]
public class DataImportServiceTests : IClassFixture<PostGisFixture>, IAsyncLifetime
{
    private readonly PostGisFixture _fixture;
    private string _tempDataDir = null!;

    public DataImportServiceTests(PostGisFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "ChiroTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDataDir);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_tempDataDir))
        {
            Directory.Delete(_tempDataDir, true);
        }
        await Task.CompletedTask;
    }

    [Fact]
    public async Task ImportTaxref_WithReferencedCdNoms_InsertsOnlyReferencedTaxons()
    {
        // Arrange
        await using var db = _fixture.CreateDbContext();
        var service = new DataImportService(db, _tempDataDir);
        
        // Prepare a dummy TAXREF.txt
        var taxrefDir = Path.Combine(_tempDataDir, "TAXREF");
        Directory.CreateDirectory(taxrefDir);
        var taxrefPath = Path.Combine(taxrefDir, "TAXREF.txt");
        
        var header = "CD_NOM\tCD_REF\tNOM_SCIENTIFIQUE\tNOM_COMMUN\tGROUP1_INPN\tGROUP2_INPN\tRANG\tHABITAT\tFRANCE_M\n";
        var line1 = "123\t123\tLynx lynx\tLynx boréal\tMammifères\tMammifères\tES\t1\tP\n";
        var line2 = "456\t456\tRana temporaria\tGrenouille rousse\tAmphibiens\tAmphibiens\tES\t1\tP\n";
        var line3 = "999\t999\tIgnore me\t—\t—\t—\tES\t1\tP\n";
        File.WriteAllText(taxrefPath, header + line1 + line2 + line3);

        var referencedCdNoms = new HashSet<int> { 123, 456 };

        // Act
        // Using Reflection or making the method public for testing
        // For this demo, we assume we are testing a public wrapper or the main pipeline
        // Here we call the private method via reflection or just test the result of a higher level method 
        // Let's assume we made it public or internal
        var method = typeof(DataImportService).GetMethod("ImportTaxref", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var result = (Dictionary<int, int>)method!.Invoke(service, new object[] { referencedCdNoms })!;

        // Assert
        db.Taxons.Count().Should().Be(2, "Only referenced CD_NOMs should be imported");
        result.Should().ContainKey(123);
        result.Should().ContainKey(456);
        result.Should().NotContainKey(999);
    }
}
