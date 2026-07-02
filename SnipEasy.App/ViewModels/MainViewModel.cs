using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.ViewModels;

/// <summary>
/// Main ViewModel for the application window.
/// Coordinates between child ViewModels and manages application lifecycle.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly AppSettingsService _settingsService;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isCapturing;

    public MainViewModel(
        AppPaths paths,
        AppLogger logger,
        AppSettingsService settingsService,
        SettingsViewModel settingsViewModel,
        RecordingViewModel recordingViewModel,
        HistoryViewModel historyViewModel)
    {
        _paths = paths;
        _logger = logger;
        _settingsService = settingsService;
        _settings = settingsService.Load();

        Settings = settingsViewModel;
        Recording = recordingViewModel;
        History = historyViewModel;

        _logger.LogEnvironment(_paths);
        StatusText = "准备就绪。按 F1 进入区域截图，按 F2 开始或停止录屏。";
    }

    /// <summary>
    /// Gets the settings ViewModel.
    /// </summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Gets the recording ViewModel.
    /// </summary>
    public RecordingViewModel Recording { get; }

    /// <summary>
    /// Gets the history ViewModel.
    /// </summary>
    public HistoryViewModel History { get; }

    /// <summary>
    /// Gets the application settings for direct access.
    /// </summary>
    public AppSettings AppSettings => _settings;

    /// <summary>
    /// Gets the application paths.
    /// </summary>
    public AppPaths Paths => _paths;

    [RelayCommand]
    private void RefreshStatus(string message)
    {
        StatusText = message;
        _logger.Info($"Status: {message}");
    }

    [RelayCommand]
    private void OpenSaveDirectory()
    {
        var directory = ScreenCaptureService.ResolveScreenshotDirectory(_settings);
        System.IO.Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenVideoDirectory()
    {
        var directory = ScreenCaptureService.ResolveRecordingDirectory(_settings);
        System.IO.Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void ExportDiagnostics()
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "导出 SnipEasy 诊断包",
            Filter = "ZIP 文件|*.zip|所有文件|*.*",
            FileName = $"SnipEasy-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            var diagnosticService = new DiagnosticPackageService(_paths, _logger);
            diagnosticService.Export(dialog.FileName, FfmpegRecordingService.ResolveFfmpegPath(_settings), Settings.EngineStatusText);
            StatusText = $"诊断包已导出：{dialog.FileName}";
        }
        catch (Exception ex)
        {
            _logger.Error("Export diagnostics failed.", ex);
            StatusText = $"导出诊断包失败：{ex.Message}";
        }
    }
}
