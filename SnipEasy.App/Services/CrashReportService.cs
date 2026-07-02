using System.IO;

namespace SnipEasy.App.Services;

public sealed class CrashReportService
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly DiagnosticPackageService _diagnosticPackageService;

    public CrashReportService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _diagnosticPackageService = new DiagnosticPackageService(paths, logger);
    }

    public string CrashDirectory
    {
        get
        {
            var directory = Path.Combine(_paths.DataDirectory, "CrashReports");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public string LatestMarkerPath => Path.Combine(CrashDirectory, "latest-crash.txt");

    public string WriteCrashReport(Exception exception, string context)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var reportPath = Path.Combine(CrashDirectory, $"crash-{timestamp}.txt");
        var packagePath = Path.Combine(CrashDirectory, $"crash-{timestamp}.zip");
        File.WriteAllText(reportPath, $"Context: {context}{Environment.NewLine}Time: {DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        _diagnosticPackageService.Export(packagePath, "", $"Crash context: {context}");
        File.WriteAllText(LatestMarkerPath, packagePath);
        _logger.Error($"Crash report generated: {packagePath}", exception);
        return packagePath;
    }

    public string ConsumeLatestCrashPackage()
    {
        if (!File.Exists(LatestMarkerPath))
        {
            return "";
        }

        try
        {
            var path = File.ReadAllText(LatestMarkerPath).Trim();
            File.Delete(LatestMarkerPath);
            return File.Exists(path) ? path : "";
        }
        catch
        {
            return "";
        }
    }
}
