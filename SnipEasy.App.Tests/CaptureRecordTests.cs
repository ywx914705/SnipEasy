using SnipEasy.App.Models;

namespace SnipEasy.App.Tests;

public class CaptureRecordTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void FileSizeDisplay_1536Bytes_FormatsAsKB()
    {
        // Arrange
        var record = new CaptureRecord { FileSizeBytes = 1536 };

        // Act
        var display = record.FileSizeDisplay;

        // Assert
        Assert.Equal("1.5 KB", display);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ClipboardDisplay_ImageMode_ReturnsChinese()
    {
        // Arrange
        var record = new CaptureRecord { ClipboardMode = "Image" };

        // Act
        var display = record.ClipboardDisplay;

        // Assert
        Assert.Equal("图片", display);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void KindDisplay_Screenshot_ReturnsChinese()
    {
        // Arrange
        var record = new CaptureRecord { Kind = CaptureKind.Screenshot };

        // Act
        var display = record.KindDisplay;

        // Assert
        Assert.Equal("截图", display);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void FileNameDisplay_ValidPath_ReturnsFileName()
    {
        // Arrange
        var record = new CaptureRecord { FilePath = @"C:\Temp\a.png" };

        // Act
        var display = record.FileNameDisplay;

        // Assert
        Assert.Equal("a.png", display);
    }

    [Theory]
    [Trait("Category", "Unit")]
    [InlineData(0, "-")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    public void FileSizeDisplay_VariousSizes_FormatsCorrectly(long bytes, string expected)
    {
        // Arrange
        var record = new CaptureRecord { FileSizeBytes = bytes };

        // Act
        var display = record.FileSizeDisplay;

        // Assert
        Assert.Equal(expected, display);
    }
}
