using SnipEasy.App.Models;
using SnipEasy.App.Services;
using SnipEasy.App.Storage;

namespace SnipEasy.App.Tests;

public class JsonFileStoreTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LoadOrDefault_BrokenFile_BackupsAndReturnsDefault()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "settings.json");
        File.WriteAllText(path, "{broken");
        var logger = new AppLogger(Path.Combine(_scope.Root, "log.txt"));
        var store = new JsonFileStore<AppSettings>(path, logger);

        // Act
        var settings = store.LoadOrDefault(() => new AppSettings { ScreenshotDirectory = "default" });

        // Assert
        Assert.Equal("default", settings.ScreenshotDirectory);
        Assert.Single(Directory.GetFiles(_scope.Root, "settings.json.broken-*"));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LoadOrDefault_ValidFile_ReturnsDeserialized()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "settings.json");
        File.WriteAllText(path, "{\"ScreenshotDirectory\":\"custom\"}");
        var store = new JsonFileStore<AppSettings>(path);

        // Act
        var settings = store.LoadOrDefault(() => new AppSettings());

        // Assert
        Assert.Equal("custom", settings.ScreenshotDirectory);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void LoadOrDefault_MissingFile_ReturnsDefault()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "nonexistent.json");
        var store = new JsonFileStore<AppSettings>(path);

        // Act
        var settings = store.LoadOrDefault(() => new AppSettings { ScreenshotDirectory = "default" });

        // Assert
        Assert.Equal("default", settings.ScreenshotDirectory);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Save_CreatesFileWithContent()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "output.json");
        var store = new JsonFileStore<AppSettings>(path);
        var settings = new AppSettings { ScreenshotDirectory = "test" };

        // Act
        store.Save(settings);

        // Assert
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("\"ScreenshotDirectory\": \"test\"", content);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Save_ExistingFile_ReplacesContentWithoutLeavingTemporaryFiles()
    {
        var path = Path.Combine(_scope.Root, "output.json");
        var store = new JsonFileStore<AppSettings>(path);
        store.Save(new AppSettings { ScreenshotDirectory = "first" });

        store.Save(new AppSettings { ScreenshotDirectory = "second" });

        var content = File.ReadAllText(path);
        Assert.Contains("\"ScreenshotDirectory\": \"second\"", content);
        Assert.False(File.Exists($"{path}.tmp"));
        Assert.False(File.Exists($"{path}.bak"));
    }
}
