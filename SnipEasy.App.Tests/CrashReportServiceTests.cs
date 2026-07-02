using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class CrashReportServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void WriteCrashReport_CreatesPackageFile()
    {
        // Arrange
        var paths = CreateTestPaths(_scope.Root);
        var service = new CrashReportService(paths, new AppLogger(paths.LogPath));

        // Act
        var package = service.WriteCrashReport(new InvalidOperationException("boom"), "test");

        // Assert
        Assert.True(File.Exists(package));
        Assert.EndsWith(".zip", package);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConsumeLatestCrashPackage_AfterWrite_ReturnsPath()
    {
        // Arrange
        var paths = CreateTestPaths(_scope.Root);
        var service = new CrashReportService(paths, new AppLogger(paths.LogPath));
        var package = service.WriteCrashReport(new InvalidOperationException("boom"), "test");

        // Act
        var consumed = service.ConsumeLatestCrashPackage();

        // Assert
        Assert.Equal(package, consumed);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ConsumeLatestCrashPackage_CalledTwice_ReturnsEmptySecondTime()
    {
        // Arrange
        var paths = CreateTestPaths(_scope.Root);
        var service = new CrashReportService(paths, new AppLogger(paths.LogPath));
        service.WriteCrashReport(new InvalidOperationException("boom"), "test");

        // Act
        service.ConsumeLatestCrashPackage();
        var secondCall = service.ConsumeLatestCrashPackage();

        // Assert
        Assert.Equal("", secondCall);
    }

    private static AppPaths CreateTestPaths(string root)
    {
        return new AppPaths
        {
            DataDirectory = root,
            SettingsPath = Path.Combine(root, "settings.json"),
            HistoryPath = Path.Combine(root, "history.json"),
            LogPath = Path.Combine(root, "log.txt"),
            ProductRootDirectory = root,
            DefaultScreenshotDirectory = root,
            DefaultVideoDirectory = root
        };
    }
}
