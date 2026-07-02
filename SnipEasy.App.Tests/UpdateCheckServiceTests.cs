using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class UpdateCheckServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose()
    {
        _scope.Dispose();
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
