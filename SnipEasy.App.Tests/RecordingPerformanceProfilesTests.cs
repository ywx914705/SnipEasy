using System.IO.Compression;
using SnipEasy.App.Models;
using SnipEasy.App.Services;
using SnipEasy.App.Storage;

namespace SnipEasy.App.Tests;

public class RecordingPerformanceProfilesTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void NormalizeMode_UnknownMode_ReturnsSmooth()
    {
        // Act
        var result = RecordingPerformanceProfiles.NormalizeMode("missing");

        // Assert
        Assert.Equal(RecordingPerformanceProfiles.Smooth, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ApplyDefaults_QualityMode_SetsCorrectValues()
    {
        // Arrange
        var settings = new AppSettings { RecordingPerformanceMode = RecordingPerformanceProfiles.Quality };

        // Act
        RecordingPerformanceProfiles.ApplyDefaults(settings);

        // Assert
        Assert.Equal(RecordingPerformanceProfiles.Quality, settings.RecordingPerformanceMode);
        Assert.Equal(24, settings.RecordingFrameRate);
        Assert.Equal(22, settings.RecordingCrf);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(RecordingPerformanceProfiles.Smooth, 8, 30)]
    [InlineData(RecordingPerformanceProfiles.Balanced, 12, 26)]
    [InlineData(RecordingPerformanceProfiles.Quality, 24, 22)]
    public void Resolve_ValidMode_ReturnsCorrectProfile(string mode, int expectedFrameRate, int expectedCrf)
    {
        // Act
        var profile = RecordingPerformanceProfiles.Resolve(mode);

        // Assert
        Assert.Equal(expectedFrameRate, profile.FrameRate);
        Assert.Equal(expectedCrf, profile.Crf);
    }
}
