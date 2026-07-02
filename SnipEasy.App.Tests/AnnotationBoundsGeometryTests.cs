using System.Windows;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class AnnotationBoundsGeometryTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void Resize_TopLeft_ExceedsBounds_ClampsToZero()
    {
        // Arrange
        var bounds = new Rect(10, 10, 30, 30);

        // Act
        var resized = AnnotationBoundsGeometry.Resize(bounds, -100, 200, "TopLeft", 100, 100);

        // Assert
        Assert.Equal(0.0, resized.Left);
        Assert.True(resized.Width >= AnnotationBoundsGeometry.MinimumElementSize);
        Assert.True(resized.Height >= AnnotationBoundsGeometry.MinimumElementSize);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resize_BottomRight_WithinBounds_Expands()
    {
        // Arrange
        var bounds = new Rect(10, 10, 30, 30);

        // Act
        var resized = AnnotationBoundsGeometry.Resize(bounds, 20, 20, "BottomRight", 100, 100);

        // Assert
        Assert.Equal(10.0, resized.Left);
        Assert.Equal(10.0, resized.Top);
        Assert.Equal(50.0, resized.Width);
        Assert.Equal(50.0, resized.Height);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Resize_BeyondCanvas_ClampsToCanvas()
    {
        // Arrange
        var bounds = new Rect(50, 50, 30, 30);

        // Act
        var resized = AnnotationBoundsGeometry.Resize(bounds, 100, 100, "BottomRight", 100, 100);

        // Assert
        Assert.True(resized.Right <= 100);
        Assert.True(resized.Bottom <= 100);
    }
}
