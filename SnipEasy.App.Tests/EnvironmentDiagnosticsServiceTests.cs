using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

public class EnvironmentDiagnosticsServiceTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public void BuildReport_ContainsScreenInfo()
    {
        // Arrange
        var service = new EnvironmentDiagnosticsService();

        // Act
        var report = service.BuildReport();

        // Assert
        Assert.Contains("Screen count", report);
        Assert.Contains("Remote session likely", report);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void BuildReport_ContainsDotNetInfo()
    {
        // Arrange
        var service = new EnvironmentDiagnosticsService();

        // Act
        var report = service.BuildReport();

        // Assert
        Assert.Contains(".NET", report);
    }
}
