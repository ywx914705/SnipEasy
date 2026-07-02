using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class ScreenCaptureServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void ExpandWatermark_WithVariables_ExpandsCorrectly()
    {
        // Arrange
        var template = "{UserName}-{MachineName}-{Timestamp}";

        // Act
        var result = ScreenCaptureService.ExpandWatermark(template);

        // Assert
        Assert.Contains(Environment.UserName, result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Environment.MachineName, result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{Timestamp}", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExpandWatermark_EmptyTemplate_ReturnsEmpty()
    {
        // Act
        var result = ScreenCaptureService.ExpandWatermark("");

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveScreenshotDirectory_EmptySetting_ReturnsDefault()
    {
        var settings = new AppSettings { ScreenshotDirectory = "" };

        var result = ScreenCaptureService.ResolveScreenshotDirectory(settings);

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Screenshots", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveScreenshotDirectory_CustomPath_ReturnsCustom()
    {
        var settings = new AppSettings { ScreenshotDirectory = @"D:\MyScreenshots" };

        var result = ScreenCaptureService.ResolveScreenshotDirectory(settings);

        Assert.Equal(@"D:\MyScreenshots", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveRecordingDirectory_EmptySetting_ReturnsDefault()
    {
        var settings = new AppSettings { VideoDirectory = "" };

        var result = ScreenCaptureService.ResolveRecordingDirectory(settings);

        Assert.False(string.IsNullOrWhiteSpace(result));
        Assert.Contains("Videos", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveRecordingDirectory_CustomPath_ReturnsCustom()
    {
        var settings = new AppSettings { VideoDirectory = @"D:\MyVideos" };

        var result = ScreenCaptureService.ResolveRecordingDirectory(settings);

        Assert.Equal(@"D:\MyVideos", result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ResolveScreenshotDirectory_WhitespaceSetting_ReturnsDefault()
    {
        var settings = new AppSettings { ScreenshotDirectory = "   " };

        var result = ScreenCaptureService.ResolveScreenshotDirectory(settings);

        Assert.False(string.IsNullOrWhiteSpace(result));
    }
}
