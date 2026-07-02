using System.IO;
using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

[Trait("Category", "Unit")]
public class AppSettingsServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();

    public void Dispose() => _scope.Dispose();

    private AppSettingsService CreateService()
    {
        var paths = new AppPaths
        {
            DataDirectory = _scope.Root,
            SettingsPath = Path.Combine(_scope.Root, "settings.json"),
            HistoryPath = Path.Combine(_scope.Root, "history.json"),
            LogPath = Path.Combine(_scope.Root, "log.txt"),
            ProductRootDirectory = _scope.Root,
            DefaultScreenshotDirectory = Path.Combine(_scope.Root, "Screenshots"),
            DefaultVideoDirectory = Path.Combine(_scope.Root, "Videos")
        };
        var logger = new AppLogger(paths.LogPath);
        return new AppSettingsService(paths, logger);
    }

    [Fact]
    public void Load_DefaultSettings_HasCorrectDefaults()
    {
        var service = CreateService();

        var settings = service.Load();

        Assert.False(string.IsNullOrWhiteSpace(settings.RecordingPerformanceMode));
        Assert.True(settings.RecordingFrameRate >= 1 && settings.RecordingFrameRate <= 30);
        Assert.True(settings.RecordingCrf >= 18 && settings.RecordingCrf <= 35);
    }

    [Fact]
    public void Load_LegacyWatermarkTemplate_ClearsWatermark()
    {
        var service = CreateService();
        var settings = service.Load();
        settings.WatermarkTemplate = "{UserName} | {MachineName} | {Timestamp}";
        settings.EnableWatermark = true;
        service.Save(settings);

        var loaded = service.Load();

        Assert.Equal("", loaded.WatermarkTemplate);
        Assert.False(loaded.EnableWatermark);
    }

    [Fact]
    public void Load_EmptyPerformanceMode_AppliesDefaults()
    {
        var service = CreateService();
        var settings = service.Load();
        settings.RecordingPerformanceMode = "";
        settings.AllowLocalAviFallback = true;
        service.Save(settings);

        var loaded = service.Load();

        Assert.False(string.IsNullOrWhiteSpace(loaded.RecordingPerformanceMode));
        Assert.False(loaded.AllowLocalAviFallback);
    }

    [Fact]
    public void Load_NonDefaultSaveDirectory_MigratesToScreenshotDirectory()
    {
        var service = CreateService();
        var settings = service.Load();
        settings.ScreenshotDirectory = "";
        settings.SaveDirectory = @"D:\CustomCaptures";
        service.Save(settings);

        var loaded = service.Load();

        Assert.Equal(@"D:\CustomCaptures", loaded.ScreenshotDirectory);
    }

    [Fact]
    public void Load_AlreadyMigratedSettings_DoesNotReMigrate()
    {
        var service = CreateService();
        var settings = service.Load();
        settings.ScreenshotDirectory = @"D:\MyScreenshots";
        settings.RecordingPerformanceMode = "Quality";
        settings.RecordingFrameRate = 24;
        settings.RecordingCrf = 22;
        service.Save(settings);

        var loaded = service.Load();

        Assert.Equal(@"D:\MyScreenshots", loaded.ScreenshotDirectory);
        Assert.Equal("Quality", loaded.RecordingPerformanceMode);
        Assert.Equal(24, loaded.RecordingFrameRate);
        Assert.Equal(22, loaded.RecordingCrf);
    }

    [Fact]
    public void Save_ThenLoad_PersistsAllSettings()
    {
        var service = CreateService();
        var settings = service.Load();
        settings.ScreenshotDirectory = @"D:\Test";
        settings.VideoDirectory = @"D:\Videos";
        settings.RecordingFrameRate = 15;
        settings.RecordingCrf = 28;
        settings.MinimizeToTrayOnClose = false;
        settings.HistoryRetentionDays = 30;
        service.Save(settings);

        var loaded = service.Load();

        Assert.Equal(@"D:\Test", loaded.ScreenshotDirectory);
        Assert.Equal(@"D:\Videos", loaded.VideoDirectory);
        Assert.Equal(15, loaded.RecordingFrameRate);
        Assert.Equal(28, loaded.RecordingCrf);
        Assert.False(loaded.MinimizeToTrayOnClose);
        Assert.Equal(30, loaded.HistoryRetentionDays);
    }
}
