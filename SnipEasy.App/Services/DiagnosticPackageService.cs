using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;

namespace SnipEasy.App.Services;

public sealed class DiagnosticPackageService
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly EnvironmentDiagnosticsService _environmentDiagnostics = new();

    public DiagnosticPackageService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public void Export(
        string outputZipPath,
        string ffmpegPath,
        string engineStatus,
        bool includeUserData = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath) ?? ".");
        var tempRoot = Path.Combine(Path.GetTempPath(), $"SnipEasyDiagnostics_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(tempRoot, "diagnostics.txt"),
                BuildSummary(ffmpegPath, engineStatus, includeUserData),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(tempRoot, "environment.txt"), _environmentDiagnostics.BuildReport(), Encoding.UTF8);
            CopyIfExists(_paths.LogPath, Path.Combine(tempRoot, "snipeasy.log"));
            if (includeUserData)
            {
                CopyIfExists(_paths.SettingsPath, Path.Combine(tempRoot, "settings.json"));
                CopyIfExists(_paths.HistoryPath, Path.Combine(tempRoot, "history.json"));
            }

            if (File.Exists(outputZipPath))
            {
                File.Delete(outputZipPath);
            }

            ZipFile.CreateFromDirectory(tempRoot, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            _logger.Info($"Diagnostics package exported: {outputZipPath}");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private string BuildSummary(string ffmpegPath, string engineStatus, bool includeUserData)
    {
        var assembly = Assembly.GetExecutingAssembly().GetName();
        var builder = new StringBuilder();
        builder.AppendLine($"SnipEasy diagnostics generated at {DateTimeOffset.Now:O}");
        builder.AppendLine($"App version: {assembly.Version}");
        builder.AppendLine($"OS: {Environment.OSVersion}");
        builder.AppendLine($".NET: {Environment.Version}");
        builder.AppendLine($"Machine: {(includeUserData ? Environment.MachineName : "[redacted]")}");
        builder.AppendLine($"User: {(includeUserData ? Environment.UserName : "[redacted]")}");
        builder.AppendLine($"Base directory: {AppContext.BaseDirectory}");
        builder.AppendLine($"Data directory: {_paths.DataDirectory}");
        builder.AppendLine($"Settings: {_paths.SettingsPath}");
        builder.AppendLine($"History: {_paths.HistoryPath}");
        builder.AppendLine($"Log: {_paths.LogPath}");
        builder.AppendLine($"Default screenshot directory: {_paths.DefaultScreenshotDirectory}");
        builder.AppendLine($"Default video directory: {_paths.DefaultVideoDirectory}");
        builder.AppendLine($"FFmpeg path: {ffmpegPath}");
        builder.AppendLine($"Engine status: {engineStatus}");
        return builder.ToString();
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, overwrite: true);
        }
    }
}
