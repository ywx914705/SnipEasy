using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class CaptureHistoryServiceTests : IDisposable
{
    private readonly TestScope _scope = TestScope.Create();
    private readonly CaptureHistoryService _service;
    private readonly AppLogger _logger;

    public CaptureHistoryServiceTests()
    {
        _logger = new AppLogger(Path.Combine(_scope.Root, "log.txt"));
        _service = new CaptureHistoryService(Path.Combine(_scope.Root, "history.json"), _logger);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Prune_OldRecords_RemovesExpired()
    {
        // Arrange
        var fresh = new CaptureRecord
        {
            Kind = CaptureKind.Screenshot,
            CreatedAt = DateTimeOffset.Now,
            FilePath = "fresh.png",
            SourceWindowTitle = "窗口A"
        };
        var old = new CaptureRecord
        {
            Kind = CaptureKind.Recording,
            CreatedAt = DateTimeOffset.Now.AddDays(-10),
            FilePath = "old.mp4"
        };

        // Act
        var result = _service.Prune([old, fresh], retentionDays: 3);

        // Assert
        Assert.Single(result);
        Assert.Equal(fresh.Id, result[0].Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Prune_PermanentRetention_KeepsOldRecords()
    {
        var old = new CaptureRecord
        {
            Kind = CaptureKind.Screenshot,
            CreatedAt = DateTimeOffset.Now.AddYears(-5),
            FilePath = "old.png"
        };
        var fresh = new CaptureRecord
        {
            Kind = CaptureKind.Screenshot,
            CreatedAt = DateTimeOffset.Now,
            FilePath = "fresh.png"
        };

        var result = _service.Prune([old, fresh], retentionDays: 0);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => item.Id == old.Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Filter_ByKind_ReturnsMatching()
    {
        // Arrange
        var screenshot = new CaptureRecord { Kind = CaptureKind.Screenshot, SourceWindowTitle = "窗口A" };
        var recording = new CaptureRecord { Kind = CaptureKind.Recording, SourceWindowTitle = "窗口B" };

        // Act
        var result = _service.Filter([screenshot, recording], CaptureKind.Screenshot, "").ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(screenshot.Id, result[0].Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void Filter_ByQuery_ReturnsMatching()
    {
        // Arrange
        var record1 = new CaptureRecord { Kind = CaptureKind.Screenshot, SourceWindowTitle = "窗口A" };
        var record2 = new CaptureRecord { Kind = CaptureKind.Screenshot, SourceWindowTitle = "测试B" };

        // Act
        var result = _service.Filter([record1, record2], null, "窗口").ToList();

        // Assert
        Assert.Single(result);
        Assert.Equal(record1.Id, result[0].Id);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void ExportCsv_WithSpecialCharacters_EscapesCorrectly()
    {
        // Arrange
        var csvPath = Path.Combine(_scope.Root, "history.csv");
        var records = new List<CaptureRecord>
        {
            new() { SourceWindowTitle = "hello, \"world\"", FilePath = "a.png" }
        };

        // Act
        _service.ExportCsv(records, csvPath);
        var csv = File.ReadAllText(csvPath);

        // Assert
        Assert.Contains("\"hello, \"\"world\"\"\"", csv);
    }
}
