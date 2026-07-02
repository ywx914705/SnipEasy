using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.ViewModels;

/// <summary>
/// ViewModel for application settings management.
/// Handles settings persistence, validation, and UI synchronization.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly AppSettingsService _settingsService;
    private readonly StartupService _startupService = new();
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _recordingParameterHintText = "可用范围：帧率 1-30 FPS，CRF 18-35；数值越小画质越高。";

    [ObservableProperty]
    private string _ffmpegPathHintText = string.Empty;

    [ObservableProperty]
    private string _recordingModeDescription = string.Empty;

    [ObservableProperty]
    private string _screenshotDefaultText = string.Empty;

    [ObservableProperty]
    private string _videoDefaultText = string.Empty;

    [ObservableProperty]
    private string _engineStatusText = string.Empty;

    [ObservableProperty]
    private string _footerText = string.Empty;

    [ObservableProperty]
    private string _audioRouteLabel = "无音频";

    // ---- Bindable wrapper properties for two-way binding ----

    [ObservableProperty]
    private string _frameRateText = "12";

    [ObservableProperty]
    private string _crfText = "23";

    [ObservableProperty]
    private int _selectedRecordingModeIndex;

    [ObservableProperty]
    private int _selectedHistoryRetentionIndex = 1;

    public SettingsViewModel(AppPaths paths, AppLogger logger, AppSettingsService settingsService)
    {
        _paths = paths;
        _logger = logger;
        _settingsService = settingsService;
        _settings = _settingsService.Load();
        UpdateAllHints();
    }

    /// <summary>
    /// Gets the application settings for data binding.
    /// </summary>
    public AppSettings Settings => _settings;

    /// <summary>
    /// Gets the available recording performance modes.
    /// </summary>
    public string[] RecordingModes { get; } =
    [
        RecordingPerformanceProfiles.Smooth,
        RecordingPerformanceProfiles.Balanced,
        RecordingPerformanceProfiles.Quality
    ];

    /// <summary>
    /// Gets the available history retention day options.
    /// </summary>
    public int[] HistoryRetentionDays { get; } = [30, 90, 180, 365];

    /// <summary>
    /// Gets whether the startup service is enabled.
    /// </summary>
    public bool IsStartupEnabled => _startupService.IsEnabled();

    /// <summary>
    /// Loads settings into bindable properties. Call once on startup.
    /// </summary>
    public void LoadFromSettings()
    {
        FrameRateText = _settings.RecordingFrameRate.ToString();
        CrfText = _settings.RecordingCrf.ToString();
        SelectedRecordingModeIndex = ModeToSelectedIndex(_settings.RecordingPerformanceMode);
        SelectedHistoryRetentionIndex = DaysToSelectedIndex(_settings.HistoryRetentionDays);
        OnPropertyChanged(nameof(Settings));
        UpdateAllHints();
    }

    /// <summary>
    /// Reads bindable properties back into the settings model and validates.
    /// Returns false if validation fails.
    /// </summary>
    public bool ApplyToSettings()
    {
        // Validate frame rate
        if (!int.TryParse(FrameRateText?.Trim(), out var frameRate) || frameRate < 1 || frameRate > 30)
        {
            RecordingParameterHintText = "录屏帧率必须是 1-30 之间的数字。";
            return false;
        }

        // Validate CRF
        if (!int.TryParse(CrfText?.Trim(), out var crf) || crf < 18 || crf > 35)
        {
            RecordingParameterHintText = "画质参数 CRF 必须是 18-35 之间的数字。";
            return false;
        }

        _settings.RecordingFrameRate = frameRate;
        _settings.RecordingCrf = crf;
        _settings.RecordingPerformanceMode = SelectedIndexToMode(SelectedRecordingModeIndex);
        _settings.HistoryRetentionDays = SelectedIndexToDays(SelectedHistoryRetentionIndex);
        _settings.SaveDirectory = _settings.ScreenshotDirectory;

        UpdateAllHints();
        return true;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
            UpdateAllHints();
        }
        catch (Exception ex)
        {
            _logger.Error("Save settings failed.", ex);
        }
    }

    [RelayCommand]
    private void ResetSettings()
    {
        _settings.ScreenshotDirectory = string.Empty;
        _settings.VideoDirectory = string.Empty;
        _settings.EnableWatermark = false;
        _settings.WatermarkTemplate = string.Empty;
        _settings.RecordingPerformanceMode = RecordingPerformanceProfiles.Smooth;
        _settings.RecordingFrameRate = 12;
        _settings.RecordingCrf = 23;
        _settings.PreferFfmpegRecording = true;
        _settings.AllowLocalAviFallback = true;
        _settings.FfmpegPath = string.Empty;
        _settings.RecordingCaptureSystemAudio = false;
        _settings.RecordingSystemAudioDevice = "virtual-audio-capturer";
        _settings.RecordingCaptureMicrophone = false;
        _settings.RecordingMicrophoneDevice = string.Empty;
        _settings.HistoryRetentionDays = 90;
        _settings.MinimizeToTrayOnClose = true;

        _settingsService.Save(_settings);
        LoadFromSettings();
    }

    [RelayCommand]
    private void ApplyPerformanceMode(string mode)
    {
        _settings.RecordingPerformanceMode = mode;
        RecordingPerformanceProfiles.ApplyDefaults(_settings);
        SelectedRecordingModeIndex = ModeToSelectedIndex(mode);
        FrameRateText = _settings.RecordingFrameRate.ToString();
        CrfText = _settings.RecordingCrf.ToString();
        OnPropertyChanged(nameof(Settings));
        UpdateRecordingModeDescription();
        UpdateEngineStatus();
    }

    [RelayCommand]
    private void ToggleStartup(bool enabled)
    {
        try
        {
            _startupService.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            _settingsService.Save(_settings);
            OnPropertyChanged(nameof(IsStartupEnabled));
        }
        catch (Exception ex)
        {
            _logger.Error("Update startup setting failed.", ex);
        }
    }

    [RelayCommand]
    private void BrowseScreenshotDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择图片保存目录",
            SelectedPath = string.IsNullOrWhiteSpace(_settings.ScreenshotDirectory)
                ? _paths.DefaultScreenshotDirectory
                : _settings.ScreenshotDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settings.ScreenshotDirectory = dialog.SelectedPath;
            _settings.SaveDirectory = dialog.SelectedPath;
            OnPropertyChanged(nameof(Settings));
            SaveSettings();
        }
    }

    [RelayCommand]
    private void BrowseVideoDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择视频保存目录",
            SelectedPath = string.IsNullOrWhiteSpace(_settings.VideoDirectory)
                ? _paths.DefaultVideoDirectory
                : _settings.VideoDirectory
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settings.VideoDirectory = dialog.SelectedPath;
            OnPropertyChanged(nameof(Settings));
            SaveSettings();
        }
    }

    [RelayCommand]
    private void BrowseFfmpegPath()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settings.FfmpegPath = dialog.FileName;
            OnPropertyChanged(nameof(Settings));
            SaveSettings();
        }
    }

    [RelayCommand]
    private void AutoDetectFfmpeg()
    {
        _settings.FfmpegPath = string.Empty;
        var detected = FfmpegRecordingService.ResolveFfmpegPath(_settings);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _settings.FfmpegPath = detected;
            OnPropertyChanged(nameof(Settings));
            SaveSettings();
        }
    }

    /// <summary>
    /// Validates and applies settings from UI controls.
    /// </summary>
    public bool ValidateAndApply(int frameRate, int crf)
    {
        if (frameRate < 1 || frameRate > 30)
        {
            RecordingParameterHintText = "录屏帧率必须是 1-30 之间的数字。";
            return false;
        }

        if (crf < 18 || crf > 35)
        {
            RecordingParameterHintText = "画质参数 CRF 必须是 18-35 之间的数字。";
            return false;
        }

        _settings.RecordingFrameRate = frameRate;
        _settings.RecordingCrf = crf;
        UpdateAllHints();
        return true;
    }

    // ---- Index mapping helpers ----

    private static int ModeToSelectedIndex(string mode)
    {
        return RecordingPerformanceProfiles.NormalizeMode(mode) switch
        {
            "Smooth" => 0,
            "Balanced" => 1,
            "Quality" => 2,
            _ => 0
        };
    }

    private static string SelectedIndexToMode(int index)
    {
        return index switch
        {
            0 => RecordingPerformanceProfiles.Smooth,
            1 => RecordingPerformanceProfiles.Balanced,
            2 => RecordingPerformanceProfiles.Quality,
            _ => RecordingPerformanceProfiles.Smooth
        };
    }

    private static int DaysToSelectedIndex(int days)
    {
        return days switch
        {
            <= 30 => 0,
            <= 90 => 1,
            <= 180 => 2,
            _ => 3
        };
    }

    private static int SelectedIndexToDays(int index)
    {
        return index switch
        {
            0 => 30,
            1 => 90,
            2 => 180,
            3 => 365,
            _ => 90
        };
    }

    // ---- Hint update methods ----

    private void UpdateAllHints()
    {
        UpdateSettingsHints();
        UpdateRecordingModeDescription();
        UpdateDefaultPathHints();
        UpdateEngineStatus();
        UpdateAudioRouteLabel();
    }

    private void UpdateSettingsHints()
    {
        var ffmpegPath = _settings.FfmpegPath?.Trim();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            FfmpegPathHintText = $"未安装 FFmpeg 时只能无声音兼容录屏；如需录电脑声音，请把 ffmpeg.exe 放到：{FfmpegRecordingService.GetBundledFfmpegPath()}.";
            return;
        }

        if (System.IO.File.Exists(ffmpegPath))
        {
            FfmpegPathHintText = "已找到指定的 ffmpeg.exe。";
            return;
        }

        FfmpegPathHintText = "未找到这个 ffmpeg.exe；请检查路径，或留空让 SnipEasy 自动查找。";
    }

    private void UpdateRecordingModeDescription()
    {
        var profile = RecordingPerformanceProfiles.Resolve(_settings.RecordingPerformanceMode);
        RecordingModeDescription = $"{profile.Description} 当前建议参数：{profile.FrameRate} FPS / CRF {profile.Crf}";
    }

    private void UpdateDefaultPathHints()
    {
        ScreenshotDefaultText = $"留空默认：{_paths.DefaultScreenshotDirectory}";
        VideoDefaultText = $"留空默认：{_paths.DefaultVideoDirectory}";
    }

    private void UpdateEngineStatus()
    {
        var ffmpegPath = FfmpegRecordingService.ResolveFfmpegPath(_settings);
        var audioRoute = AudioRouteLabel;

        if (_settings.PreferFfmpegRecording && !string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var profile = RecordingPerformanceProfiles.Resolve(_settings.RecordingPerformanceMode);
            EngineStatusText = $"截图：区域批注可用 | 录屏：MP4 {audioRoute} · {profile.DisplayName}";
        }
        else if (_settings.AllowLocalAviFallback)
        {
            EngineStatusText = "截图：区域批注可用 | 录屏：AVI 兼容模式（无音频）；安装 FFmpeg 后可录电脑声音";
        }
        else
        {
            EngineStatusText = "截图：区域批注可用 | 录屏：等待 FFmpeg 组件";
        }

        FooterText = $"配置：{_paths.SettingsPath}";
    }

    private void UpdateAudioRouteLabel()
    {
        var routes = new List<string>();
        if (_settings.RecordingCaptureSystemAudio)
        {
            routes.Add("电脑声音");
        }

        if (_settings.RecordingCaptureMicrophone)
        {
            routes.Add("麦克风");
        }

        AudioRouteLabel = routes.Count == 0 ? "无音频" : string.Join("+", routes);
    }
}
