using System.IO.Compression;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class DiagnosticPackageServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Export_WithCoreFiles_CreatesZipWithAllEntries()
    {
        // Arrange
        var paths = CreateTestPaths(_scope.Root);
        File.WriteAllText(paths.LogPath, "log");
        File.WriteAllText(paths.SettingsPath, "{}");
        File.WriteAllText(paths.HistoryPath, "[]");
        var packagePath = Path.Combine(_scope.Root, "diagnostics.zip");
        var service = new DiagnosticPackageService(paths, new AppLogger(paths.LogPath));

        // Act
        service.Export(packagePath, "ffmpeg.exe", "engine ok");

        // Assert
        Assert.True(File.Exists(packagePath));
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("diagnostics.txt", entries);
        Assert.Contains("environment.txt", entries);
        Assert.Contains("snipeasy.log", entries);
        Assert.Contains("settings.json", entries);
        Assert.Contains("history.json", entries);
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
