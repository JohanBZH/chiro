using Chiro.App.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Chiro.Tests;

public class PostGisFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _dbContainer;
    
    // We use the postgis image to support native spatial functions.
    public PostGisFixture()
    {
        _dbContainer = new PostgreSqlBuilder()
            .WithImage("postgis/postgis:15-3.3")
            .WithDatabase("test_db")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();
    }

    public string ConnectionString => _dbContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Start the Docker container
        await _dbContainer.StartAsync();

        // Ensure the schema and extensions (postgis) are created
        var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        // Stop and remove the Docker container
        await _dbContainer.DisposeAsync();
    }

    /// <summary>
    /// Creates a fresh DbContext for isolated testing.
    /// </summary>
    public ChiroDbContext CreateDbContext()
    {
        return new ChiroDbContext(ConnectionString);
    }
}
