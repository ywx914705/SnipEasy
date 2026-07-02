using System.IO;
using SnipEasy.App.Models;
using SnipEasy.App.Storage;

namespace SnipEasy.App.Services;

public sealed class AppSettingsService
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly JsonFileStore<AppSettings> _store;

    public AppSettingsService(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _store = new JsonFileStore<AppSettings>(_paths.SettingsPath, _logger);
    }

    public AppSettings Load()
    {
        var settings = _store.LoadOrDefault(CreateDefaultSettings);
        if (MigrateLegacySettings(settings))
        {
            Save(settings);
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        _store.Save(settings);
    }

    private AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            SaveDirectory = _paths.DefaultCaptureDirectory
        };
    }

    private bool MigrateLegacySettings(AppSettings settings)
    {
        var changed = false;

        if (string.Equals(
                settings.WatermarkTemplate,
                "{UserName} | {MachineName} | {Timestamp}",
                StringComparison.OrdinalIgnoreCase))
        {
            settings.WatermarkTemplate = "";
            settings.EnableWatermark = false;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(settings.RecordingPerformanceMode))
        {
            RecordingPerformanceProfiles.ApplyDefaults(settings);
            settings.AllowLocalAviFallback = false;
            changed = true;
        }
        else
        {
            settings.RecordingPerformanceMode = RecordingPerformanceProfiles.NormalizeMode(settings.RecordingPerformanceMode);
            settings.RecordingFrameRate = Math.Clamp(settings.RecordingFrameRate, 1, 30);
            settings.RecordingCrf = Math.Clamp(settings.RecordingCrf, 18, 35);
        }

        if (!string.IsNullOrWhiteSpace(settings.ScreenshotDirectory) ||
            string.IsNullOrWhiteSpace(settings.SaveDirectory))
        {
            return changed;
        }

        var legacyProductDefault = Path.Combine(_paths.ProductRootDirectory, "Screenshots");
        var legacyDocumentsDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SnipEasy",
            "Captures");
        if (!string.Equals(settings.SaveDirectory, legacyProductDefault, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.SaveDirectory, legacyDocumentsDefault, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.SaveDirectory, _paths.DefaultScreenshotDirectory, StringComparison.OrdinalIgnoreCase))
        {
            settings.ScreenshotDirectory = settings.SaveDirectory;
            changed = true;
        }

        return changed;
    }
}
