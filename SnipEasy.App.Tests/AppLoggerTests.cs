using System.IO;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

[Trait("Category", "Unit")]
public class AppLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public AppLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SnipEasyLoggerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logPath = Path.Combine(_tempDir, "test.log");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public void Info_WritesToFile()
    {
        var logger = new AppLogger(_logPath);

        logger.Info("test message");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[INFO]", content);
        Assert.Contains("test message", content);
    }

    [Fact]
    public void Warn_WritesToFile()
    {
        var logger = new AppLogger(_logPath);

        logger.Warn("warning message");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[WARN]", content);
        Assert.Contains("warning message", content);
    }

    [Fact]
    public void Error_WritesToFile()
    {
        var logger = new AppLogger(_logPath);

        logger.Error("error message");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("error message", content);
    }

    [Fact]
    public void Error_WithException_IncludesExceptionInfo()
    {
        var logger = new AppLogger(_logPath);

        logger.Error("error message", new InvalidOperationException("test exception"));

        var content = File.ReadAllText(_logPath);
        Assert.Contains("[ERROR]", content);
        Assert.Contains("error message", content);
        Assert.Contains("test exception", content);
    }

    [Fact]
    public void MultipleWrites_AppendsToFile()
    {
        var logger = new AppLogger(_logPath);

        logger.Info("first");
        logger.Info("second");

        var content = File.ReadAllText(_logPath);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
        var lineCount = content.Split(Environment.NewLine).Length;
        Assert.True(lineCount >= 2);
    }

    [Fact]
    public void Write_IncludesTimestamp()
    {
        var logger = new AppLogger(_logPath);

        logger.Info("timestamped");

        var content = File.ReadAllText(_logPath);
        // Should contain date in yyyy-MM-dd format
        Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), content);
    }
}
