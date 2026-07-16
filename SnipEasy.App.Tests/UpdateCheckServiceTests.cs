using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class UpdateCheckServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Theory]
    [InlineData("1.2.3+build.7", "1.2.3")]
    [InlineData("2.0.0-preview", "2.0.0")]
    public void TryParseVersion_WithMetadata_ParsesNumericVersion(string value, string expected)
    {
        var parsed = UpdateCheckService.TryParseVersion(value, out var version);

        Assert.True(parsed);
        Assert.Equal(expected, version.ToString());
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Check_NewerVersionAvailable_ReturnsHasUpdate()
    {
        // Arrange
        var manifest = Path.Combine(_scope.Root, "update.json");
        File.WriteAllText(manifest, "{\"version\":\"9.9.9\",\"downloadPath\":\"setup.exe\",\"notes\":\"new\"}");
        var service = new UpdateCheckService(manifest);

        // Act
        var result = service.Check(new Version(0, 2, 0));

        // Assert
        Assert.True(result.HasUpdate);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.Equal("setup.exe", result.DownloadPath);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Check_SameVersion_ReturnsNoUpdate()
    {
        // Arrange
        var manifest = Path.Combine(_scope.Root, "update.json");
        File.WriteAllText(manifest, "{\"version\":\"0.2.0\",\"downloadPath\":\"setup.exe\",\"notes\":\"same\"}");
        var service = new UpdateCheckService(manifest);

        // Act
        var result = service.Check(new Version(0, 2, 0));

        // Assert
        Assert.False(result.HasUpdate);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Check_MissingManifest_ReturnsNoUpdate()
    {
        // Arrange
        var manifest = Path.Combine(_scope.Root, "nonexistent.json");
        var service = new UpdateCheckService(manifest);

        // Act
        var result = service.Check(new Version(0, 2, 0));

        // Assert
        Assert.False(result.HasUpdate);
    }
}
