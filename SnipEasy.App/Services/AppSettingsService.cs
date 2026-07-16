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
        changed |= NormalizeNullableValues(settings);

        var normalizedDelay = Math.Clamp(settings.CaptureDelaySeconds, 0, 10);
        if (settings.CaptureDelaySeconds != normalizedDelay)
        {
            settings.CaptureDelaySeconds = normalizedDelay;
            changed = true;
        }

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
        var legacyDDriveDefault = Path.Combine(@"D:\SnipEasy", "Screenshots");
        var legacyDocumentsDefault = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "SnipEasy",
            "Captures");
        if (!string.Equals(settings.SaveDirectory, legacyProductDefault, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.SaveDirectory, legacyDDriveDefault, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.SaveDirectory, legacyDocumentsDefault, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.SaveDirectory, _paths.DefaultScreenshotDirectory, StringComparison.OrdinalIgnoreCase))
        {
            settings.ScreenshotDirectory = settings.SaveDirectory;
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeNullableValues(AppSettings settings)
    {
        var changed = false;
        changed |= NormalizeString(settings.ScreenshotDirectory, value => settings.ScreenshotDirectory = value);
        changed |= NormalizeString(settings.VideoDirectory, value => settings.VideoDirectory = value);
        changed |= NormalizeString(settings.SaveDirectory, value => settings.SaveDirectory = value);
        changed |= NormalizeString(settings.WatermarkTemplate, value => settings.WatermarkTemplate = value);
        changed |= NormalizeString(settings.RecordingPerformanceMode, value => settings.RecordingPerformanceMode = value);
        changed |= NormalizeString(settings.FfmpegPath, value => settings.FfmpegPath = value);
        changed |= NormalizeString(settings.RecordingSystemAudioDevice, value => settings.RecordingSystemAudioDevice = value);
        changed |= NormalizeString(settings.RecordingMicrophoneDevice, value => settings.RecordingMicrophoneDevice = value);

        if (settings.Hotkeys is null)
        {
            settings.Hotkeys = new HotkeySettings();
            changed = true;
        }

        changed |= NormalizeString(settings.Hotkeys.Screenshot, value => settings.Hotkeys.Screenshot = value, "F1");
        changed |= NormalizeString(settings.Hotkeys.Recording, value => settings.Hotkeys.Recording = value, "F2");
        changed |= NormalizeString(settings.Hotkeys.Sticker, value => settings.Hotkeys.Sticker = value, "F3");
        changed |= NormalizeString(settings.Hotkeys.ColorPicker, value => settings.Hotkeys.ColorPicker = value, "F4");
        return changed;
    }

    private static bool NormalizeString(string? value, Action<string> assign, string fallback = "")
    {
        if (value is not null)
        {
            return false;
        }

        assign(fallback);
        return true;
    }
}
