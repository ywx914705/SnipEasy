using System.Windows;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class CaptureSelectionGeometryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Clamp_ExceedsBounds_ClampsToZero()
    {
        // Arrange
        var rect = new Rect(-10, 5, 120, 60);

        // Act
        var clamped = CaptureSelectionGeometry.Clamp(rect, 100, 80);

        // Assert
        Assert.Equal(0.0, clamped.Left);
        Assert.Equal(5.0, clamped.Top);
        Assert.Equal(100.0, clamped.Width);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Clamp_WithinBounds_ReturnsSame()
    {
        // Arrange
        var rect = new Rect(10, 20, 30, 40);

        // Act
        var clamped = CaptureSelectionGeometry.Clamp(rect, 100, 100);

        // Assert
        Assert.Equal(rect, clamped);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToPixelRegion_ValidRect_ScalesCorrectly()
    {
        // Arrange
        var rect = new Rect(10, 10, 50, 20);

        // Act
        var region = CaptureSelectionGeometry.ToPixelRegion(rect, 100, 100, 200, 300);

        // Assert
        Assert.Equal(20, region.X);
        Assert.Equal(30, region.Y);
        Assert.Equal(100, region.Width);
        Assert.Equal(60, region.Height);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ToPixelRegion_ZeroSize_ClampsToOne()
    {
        // Arrange
        var rect = new Rect(99, 99, 1, 1);

        // Act
        var region = CaptureSelectionGeometry.ToPixelRegion(rect, 100, 100, 100, 100);

        // Assert
        Assert.True(region.Width >= 1);
        Assert.True(region.Height >= 1);
    }
}
