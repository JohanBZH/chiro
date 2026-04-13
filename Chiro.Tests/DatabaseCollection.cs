using Xunit;

namespace Chiro.Tests;

/// <summary>
/// This class ensures that all tests marked with [Collection("Database collection")]
/// share a single instance of the PostGisFixture.
/// </summary>
[CollectionDefinition("Database collection")]
public class DatabaseCollection : ICollectionFixture<PostGisFixture>
{
}
