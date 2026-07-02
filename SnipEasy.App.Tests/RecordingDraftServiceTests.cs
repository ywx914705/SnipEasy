using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class RecordingDraftServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();
    private readonly RecordingDraftService _service;

    public RecordingDraftServiceTests()
    {
        var paths = CreateTestPaths(_scope.Root);
        _service = new RecordingDraftService(paths);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateUniqueFilePath_ExistingFile_AppendsIndex()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_scope.Root, "video.mp4"), "x");

        // Act
        var result = _service.CreateUniqueFilePath(_scope.Root, "video.mp4");

        // Assert
        Assert.Equal(Path.Combine(_scope.Root, "video_2.mp4"), result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CreateUniqueFilePath_NoConflict_ReturnsOriginal()
    {
        // Act
        var result = _service.CreateUniqueFilePath(_scope.Root, "unique.mp4");

        // Assert
        Assert.Equal(Path.Combine(_scope.Root, "unique.mp4"), result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void MoveDraft_SourceExists_MovesToDestination()
    {
        // Arrange
        var source = Path.Combine(_scope.Root, "draft.mp4");
        var dest = Path.Combine(_scope.Root, "final.mp4");
        File.WriteAllText(source, "content");

        // Act
        _service.MoveDraft(source, dest);

        // Assert
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(dest));
        Assert.Equal("content", File.ReadAllText(dest));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeleteFileIfExists_ExistingFile_Deletes()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "to_delete.mp4");
        File.WriteAllText(path, "content");

        // Act
        _service.DeleteFileIfExists(path);

        // Assert
        Assert.False(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void DeleteFileIfExists_MissingFile_NoOp()
    {
        // Arrange
        var path = Path.Combine(_scope.Root, "nonexistent.mp4");

        // Act & Assert (should not throw)
        _service.DeleteFileIfExists(path);
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
