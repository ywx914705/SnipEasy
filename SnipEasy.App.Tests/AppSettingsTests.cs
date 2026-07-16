using SnipEasy.App.Models;

namespace SnipEasy.App.Tests;

public class AppSettingsTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DefaultValues_AreCorrect()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        Assert.Equal("", settings.ScreenshotDirectory);
        Assert.Equal("", settings.VideoDirectory);
        Assert.Equal("", settings.SaveDirectory);
        Assert.False(settings.EnableWatermark);
        Assert.Equal("", settings.WatermarkTemplate);
        Assert.Equal(12, settings.RecordingFrameRate);
        Assert.Equal(23, settings.RecordingCrf);
        Assert.True(settings.PreferFfmpegRecording);
        Assert.False(settings.AllowLocalAviFallback);
        Assert.Equal(90, settings.HistoryRetentionDays);
        Assert.True(settings.MinimizeToTrayOnClose);
        Assert.False(settings.FirstRunCompleted);
        Assert.False(settings.StartWithWindows);
        Assert.Equal(0, settings.CaptureDelaySeconds);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void OnboardingAndStartupFlags_CanBeSet()
    {
        // Arrange & Act
        var settings = new AppSettings
        {
            FirstRunCompleted = true,
            StartWithWindows = true
        };

        // Assert
        Assert.True(settings.FirstRunCompleted);
        Assert.True(settings.StartWithWindows);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void RecordingSettings_CanBeModified()
    {
        // Arrange
        var settings = new AppSettings();

        // Act
        settings.RecordingFrameRate = 24;
        settings.RecordingCrf = 18;
        settings.RecordingPerformanceMode = RecordingPerformanceProfiles.Quality;

        // Assert
        Assert.Equal(24, settings.RecordingFrameRate);
        Assert.Equal(18, settings.RecordingCrf);
        Assert.Equal(RecordingPerformanceProfiles.Quality, settings.RecordingPerformanceMode);
    }
}
