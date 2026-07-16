using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SnipEasy.App.Models;
using SnipEasy.App.Native;
using SnipEasy.App.Services;
using SnipEasy.App.ViewModels;
using Forms = System.Windows.Forms;

namespace SnipEasy.App;

public partial class MainWindow : Window
{
    private const int ScreenshotHotkeyId = 9101;
    private const int RecordingHotkeyId = 9102;
    private const int StickerHotkeyId = 9103;
    private const int ColorPickerHotkeyId = 9104;
    private const string AudioDeviceManualEntryHint = "未发现设备：可手动输入 DirectShow 音频设备名";

    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly AppSettingsService _settingsService;
    private readonly DiagnosticPackageService _diagnosticPackageService;
    private readonly StartupService _startupService = new();
    private readonly ScreenCaptureService _screenCaptureService;
    private readonly WindowSelectionService _windowSelectionService;
    private readonly OcrService _ocrService;
    private readonly ClipboardService _clipboardService;
    private readonly IRecordingService _recordingService;
    private readonly AppSettings _settings;
    private readonly MainViewModel _viewModel;
    private readonly StickerManager _stickerManager;
    private HotkeyManager? _hotkeyManager;
    private TrayService? _trayService;
    private RecordingStatusWindow? _recordingStatusWindow;
    private bool _allowClose;
    private bool _captureBusy;
    private bool _isApplyingSettingsToUi;
    private bool _audioDeviceRefreshBusy;
    private bool _audioDeviceRefreshAttempted;

    public MainWindow(
        AppPaths paths,
        AppLogger logger,
        AppSettingsService settingsService,
        AppSettings settings,
        DiagnosticPackageService diagnosticPackageService,
        ScreenCaptureService screenCaptureService,
        WindowSelectionService windowSelectionService,
        OcrService ocrService,
        ClipboardService clipboardService,
        IRecordingService recordingService,
        MainViewModel viewModel,
        StickerManager stickerManager)
    {
        _paths = paths;
        _logger = logger;
        _settingsService = settingsService;
        _diagnosticPackageService = diagnosticPackageService;
        _screenCaptureService = screenCaptureService;
        _windowSelectionService = windowSelectionService;
        _ocrService = ocrService;
        _clipboardService = clipboardService;
        _recordingService = recordingService;
        _viewModel = viewModel;
        _stickerManager = stickerManager;
        _settings = settings;
        InitializeComponent();

        _logger.Info("MainWindow created.");
        DataContext = _viewModel;
        WireRecordingViewModelEvents();
        _viewModel.History.LoadHistoryCommand.Execute(null);
        ApplySettingsToUi();
        _viewModel.StatusText = "准备就绪。按 F1 进入区域截图，按 F2 开始或停止录屏。";

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private void ShowFirstRunGuideIfNeeded()
    {
        if (_settings.FirstRunCompleted)
        {
            return;
        }

        ShowFirstRunGuide(markCompleted: true);
    }

    private void ShowFirstRunGuide(bool markCompleted)
    {
        var guide = new FirstRunWindow();
        if (IsLoaded)
        {
            guide.Owner = this;
        }

        _ = guide.ShowDialog();
        if (markCompleted)
        {
            _settings.FirstRunCompleted = true;
            _settingsService.Save(_settings);
        }
    }

    private void ShowPreviousCrashReportIfAny()
    {
        var crashReportService = new CrashReportService(_paths, _logger);
        var packagePath = crashReportService.ConsumeLatestCrashPackage();
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return;
        }

        RefreshStatus($"检测到上次崩溃诊断包：{packagePath}");
        System.Windows.MessageBox.Show(
            $"检测到上次崩溃后自动生成的诊断包：{packagePath}",
            "SnipEasy 崩溃诊断",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _hotkeyManager = new HotkeyManager();
        _hotkeyManager.StatusChanged += (_, message) =>
        {
            _logger.Info(message);
            RefreshStatus(message);
        };
        _hotkeyManager.RegistrationFailed += (_, message) =>
        {
            _logger.Warn(message);
            RefreshStatus(message);
        };
        _hotkeyManager.Attach(handle);
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        var hotkeyManager = _hotkeyManager;
        if (hotkeyManager is null)
        {
            return;
        }

        var hotkeys = _settings.Hotkeys;

        // Register screenshot hotkey
        if (!hotkeyManager.RegisterFromString(ScreenshotHotkeyId, hotkeys.Screenshot, "区域截图", () => _ = StartScreenshotWorkflowAsync()))
        {
            hotkeyManager.Register(ScreenshotHotkeyId, Key.F1, 0, "F1 区域截图 (默认)", () => _ = StartScreenshotWorkflowAsync());
        }

        // Register recording hotkey
        if (!hotkeyManager.RegisterFromString(RecordingHotkeyId, hotkeys.Recording, "录屏", () => _ = _viewModel.Recording.ToggleRecordingCommand.ExecuteAsync(null)))
        {
            hotkeyManager.Register(RecordingHotkeyId, Key.F2, 0, "F2 录屏 (默认)", () => _ = _viewModel.Recording.ToggleRecordingCommand.ExecuteAsync(null));
        }

        // Register sticker hotkey
        if (!hotkeyManager.RegisterFromString(StickerHotkeyId, hotkeys.Sticker, "贴图", StartStickerFromClipboard))
        {
            hotkeyManager.Register(StickerHotkeyId, Key.F3, 0, "F3 贴图 (默认)", StartStickerFromClipboard);
        }

        // Register color picker hotkey
        if (!hotkeyManager.RegisterFromString(ColorPickerHotkeyId, hotkeys.ColorPicker, "屏幕取色", StartColorPicker))
        {
            hotkeyManager.Register(ColorPickerHotkeyId, Key.F4, 0, "F4 屏幕取色 (默认)", StartColorPicker);
        }
    }

    /// <summary>
    /// Updates hotkey registrations based on current settings.
    /// </summary>
    public void UpdateHotkeys()
    {
        _hotkeyManager?.Unregister(ScreenshotHotkeyId);
        _hotkeyManager?.Unregister(RecordingHotkeyId);
        _hotkeyManager?.Unregister(StickerHotkeyId);
        _hotkeyManager?.Unregister(ColorPickerHotkeyId);
        RegisterHotkeys();
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _trayService ??= new TrayService(
            ShowAndActivate,
            () => Dispatcher.Invoke(() => _ = StartScreenshotWorkflowAsync()),
            () => Dispatcher.Invoke(() => _ = _viewModel.Recording.ToggleRecordingCommand.ExecuteAsync(null)),
            OpenSaveDirectory,
            ExitApplication);

        _stickerManager.CleanupOldImages();
        _stickerManager.RestoreState();

        Dispatcher.BeginInvoke(() =>
        {
            ShowPreviousCrashReportIfAny();
            ShowFirstRunGuideIfNeeded();
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private async void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        await StartScreenshotWorkflowAsync();
    }

    private async void RecordingButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.Recording.ToggleRecordingCommand.ExecuteAsync(null);
    }

    private void StickerCardButton_Click(object sender, RoutedEventArgs e)
    {
        StartStickerFromClipboard();
    }

    private void ColorPickerCardButton_Click(object sender, RoutedEventArgs e)
    {
        StartColorPicker();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ApplySettingsFromUi())
        {
            SaveSettings();
            RefreshStatus("设置已保存。");
        }
    }

    private void ShowFirstRunGuide_Click(object sender, RoutedEventArgs e)
    {
        ShowFirstRunGuide(markCompleted: false);
    }

    private void StartWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_isApplyingSettingsToUi || StartWithWindowsCheckBox is null)
        {
            return;
        }

        try
        {
            _startupService.SetEnabled(StartWithWindowsCheckBox.IsChecked == true);
            _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            SaveSettings();
            RefreshStatus(_settings.StartWithWindows ? "已启用开机启动。" : "已关闭开机启动。");
        }
        catch (Exception ex)
        {
            _logger.Error("Update startup setting failed.", ex);
            RefreshStatus($"开机启动设置失败：{ex.Message}");
        }
    }

    private void RecordingModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isApplyingSettingsToUi || RecordingModeComboBox.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return;
        }

        var mode = item.Tag?.ToString() ?? RecordingPerformanceProfiles.Smooth;
        _settings.RecordingPerformanceMode = mode;
        RecordingPerformanceProfiles.ApplyDefaults(_settings);
        FrameRateTextBox.Text = _settings.RecordingFrameRate.ToString();
        CrfTextBox.Text = _settings.RecordingCrf.ToString();
        UpdateRecordingModeDescription();
        UpdateEngineStatus();
    }

    private void BrowseScreenshotDirectory_Click(object sender, RoutedEventArgs e)
    {
        BrowseDirectory(
            "选择图片保存目录",
            string.IsNullOrWhiteSpace(ScreenshotDirectoryTextBox.Text) ? _paths.DefaultScreenshotDirectory : ScreenshotDirectoryTextBox.Text,
            selected => ScreenshotDirectoryTextBox.Text = selected);
    }

    private void BrowseVideoDirectory_Click(object sender, RoutedEventArgs e)
    {
        BrowseDirectory(
            "选择视频保存目录",
            string.IsNullOrWhiteSpace(VideoDirectoryTextBox.Text) ? _paths.DefaultVideoDirectory : VideoDirectoryTextBox.Text,
            selected => VideoDirectoryTextBox.Text = selected);
    }

    private void BrowseDirectory(string description, string selectedPath, Action<string> apply)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            SelectedPath = selectedPath
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            apply(dialog.SelectedPath);
            _ = ApplySettingsFromUi();
            SaveSettings();
        }
    }

    private void OpenSaveDirectory_Click(object sender, RoutedEventArgs e)
    {
        OpenSaveDirectory();
    }

    private void OpenVideoDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplySettingsFromUi())
        {
            return;
        }

        OpenDirectory(ScreenCaptureService.ResolveRecordingDirectory(_settings));
    }

    private void OpenSelectedFile_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedFile();
    }

    private void CopySelectedFile_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CaptureRecord record || !File.Exists(record.FilePath))
        {
            RefreshStatus("请先选择一条存在的历史记录。");
            return;
        }

        _clipboardService.SetFileDrop(record.FilePath);
        RefreshStatus("文件已复制到剪贴板。");
    }

    private void CopySelectedPath_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CaptureRecord record || string.IsNullOrWhiteSpace(record.FilePath))
        {
            RefreshStatus("请先选择一条历史记录。");
            return;
        }

        System.Windows.Clipboard.SetText(record.FilePath);
        RefreshStatus("文件路径已复制到剪贴板。");
    }

    private void RemoveSelectedHistory_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CaptureRecord)
        {
            RefreshStatus("请先选择一条历史记录。");
            return;
        }

        _viewModel.History.RemoveSelectedRecordCommand.Execute(null);
        RefreshStatus("已移除选中的历史记录，原文件不会被删除。");
    }

    private void CleanMissingHistory_Click(object sender, RoutedEventArgs e)
    {
        var before = _viewModel.History.TotalCount;
        _viewModel.History.CleanMissingRecordsCommand.Execute(null);
        var removed = before - _viewModel.History.TotalCount;
        if (removed == 0)
        {
            RefreshStatus("没有发现文件已不存在的历史记录。");
            return;
        }

        RefreshStatus($"已清理 {removed} 条文件不存在的历史记录。");
    }

    private void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedFile();
    }

    private void StartStickerFromClipboard()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                RefreshStatus("剪贴板中没有图片，无法创建贴图。");
                return;
            }

            var image = System.Windows.Clipboard.GetImage();
            if (image is null)
            {
                RefreshStatus("无法从剪贴板获取图片。");
                return;
            }

            var stickerManager = _stickerManager;
            stickerManager.CreateSticker(image);
            RefreshStatus("已从剪贴板创建贴图。");
            _logger.Info("Sticker created from clipboard.");
        }
        catch (Exception ex)
        {
            _logger.Error("Create sticker from clipboard failed.", ex);
            RefreshStatus($"创建贴图失败：{ex.Message}");
        }
    }

    private void StartColorPicker()
    {
        try
        {
            _logger.Info("Color picker started.");
            RefreshStatus("正在启动取色器，请移动鼠标到目标颜色上...");

            var picker = ColorPickerWindow.ShowPicker();
            picker.ColorSelected += (s, color) =>
            {
                var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                _logger.Info($"Color selected: {hex}");
                RefreshStatus($"已选择颜色：{hex}，已复制到剪贴板。");
                _trayService?.ShowTip("取色完成", $"颜色 {hex} 已复制到剪贴板。");
            };
        }
        catch (Exception ex)
        {
            _logger.Error("Color picker failed.", ex);
            RefreshStatus($"取色器启动失败：{ex.Message}");
        }
    }

    private async Task StartScreenshotWorkflowAsync()
    {
        if (_captureBusy)
        {
            _logger.Info("Screenshot request ignored because a capture is already running.");
            return;
        }

        if (!ApplySettingsFromUi())
        {
            _logger.Warn("Screenshot request ignored because settings are invalid.");
            return;
        }

        _captureBusy = true;
        CaptureButton.IsEnabled = false;
        _logger.Info("Region screenshot workflow started.");
        RefreshStatus("正在进入截图模式，请框选区域后直接批注、复制或保存。");
        var restoreMainWindow = IsVisible;
        var copiedToClipboard = false;

        try
        {
            if (restoreMainWindow)
            {
                Hide();
            }

            var delaySeconds = Math.Clamp(_settings.CaptureDelaySeconds, 0, 10);
            if (delaySeconds > 0)
            {
                RefreshStatus($"{delaySeconds} 秒后开始截图，请切换到目标窗口。");
                _trayService?.ShowTip("延时截图", $"{delaySeconds} 秒后开始截图。");
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
            }

            await Task.Delay(160);
            var sourceWindow = NativeMethods.GetForegroundWindowInfo();
            var desktopSnapshot = await Task.Run(() => _screenCaptureService.CaptureVirtualDesktop());
            var selector = new RegionCaptureWindow(
                desktopSnapshot,
                _settings.EnableWatermark,
                ScreenCaptureService.ExpandWatermark(_settings.WatermarkTemplate),
                _windowSelectionService)
            {
                Owner = restoreMainWindow ? this : null
            };

            var completed = selector.ShowDialog() == true && selector.AnnotatedImage is not null;
            if (!completed)
            {
                RefreshStatus("已取消截图。");
                return;
            }

            var image = selector.AnnotatedImage!;
            CaptureRecord? savedRecord = null;
            var statusText = "";
            var tipText = "";

            if (selector.RequestedAction == RegionCaptureWindow.CaptureAction.CopyToClipboard)
            {
                _clipboardService.SetImage(image);
                copiedToClipboard = true;
                statusText = "截图已复制到剪贴板。";
                tipText = "图片已复制，可直接粘贴。";
            }
            else if (selector.RequestedAction == RegionCaptureWindow.CaptureAction.Sticker)
            {
                _stickerManager.CreateSticker(image);
                statusText = "截图已贴到屏幕。";
                tipText = "贴图已创建，可拖拽移动。";
            }
            else if (selector.RequestedAction == RegionCaptureWindow.CaptureAction.Save)
            {
                var screenshotDirectory = ScreenCaptureService.ResolveScreenshotDirectory(_settings);
                var result = _screenCaptureService.SaveScreenshotToDirectory(
                    image,
                    screenshotDirectory,
                    selector.IsWatermarkApplied,
                    sourceWindow,
                    "File",
                    "Image Directory");
                savedRecord = result.Record;
                statusText = $"截图已保存到图片目录：{Path.GetFileName(result.Record.FilePath)}";
                tipText = $"已保存到：{screenshotDirectory}";
            }
            else if (selector.RequestedAction == RegionCaptureWindow.CaptureAction.ExtractText)
            {
                RefreshStatus("正在本地识别文字...");
                var recognition = await _ocrService.RecognizeAsync(image);
                if (!recognition.IsSuccessful)
                {
                    RefreshStatus(recognition.Message);
                    _trayService?.ShowTip("文字识别", recognition.Message);
                    return;
                }

                _clipboardService.SetText(recognition.Text);
                statusText = recognition.Message;
                tipText = $"已复制 {recognition.Text.Length} 个字符。";
            }
            else
            {
                RefreshStatus("已取消截图。");
                return;
            }

            if (savedRecord is not null)
            {
                _viewModel.History.AddRecordCommand.Execute(savedRecord);
            }

            _logger.Info(savedRecord is null
                ? "Region screenshot copied to clipboard."
                : $"Region screenshot completed: {savedRecord.FilePath}");

            RefreshStatus(statusText);
            _trayService?.ShowTip("截图完成", tipText);
        }
        catch (Exception ex)
        {
            _logger.Error("Region screenshot workflow failed.", ex);
            RefreshStatus($"截图失败：{ex.Message}");
        }
        finally
        {
            CaptureButton.IsEnabled = true;
            _captureBusy = false;
            if (ShouldRestoreMainWindowAfterCapture(restoreMainWindow, copiedToClipboard) &&
                IsLoaded &&
                !IsVisible)
            {
                ShowAndActivate();
            }
        }
    }

    internal static bool ShouldRestoreMainWindowAfterCapture(
        bool wasVisibleBeforeCapture,
        bool copiedToClipboard)
    {
        return wasVisibleBeforeCapture && !copiedToClipboard;
    }

    /// <summary>
    /// Wires up RecordingViewModel events to View-layer operations.
    /// </summary>
    private void WireRecordingViewModelEvents()
    {
        var rec = _viewModel.Recording;
        rec.ShowRecordingStatusRequested += (_, args) =>
        {
            Dispatcher.Invoke(() => ShowRecordingStatusWindow(args.startedAt, args.subtitle));
        };
        rec.CloseRecordingStatusRequested += (_, _) =>
        {
            Dispatcher.Invoke(CloseRecordingStatusWindow);
        };
        rec.ShowDecisionRequested += (_, args) =>
        {
            Dispatcher.Invoke(() => ShowRecordingDecision(args.fileName, args.saveDirectory));
        };
        rec.StatusRefreshRequested += (_, message) =>
        {
            Dispatcher.Invoke(() => RefreshStatus(message));
        };
        rec.TrayTipRequested += (_, args) =>
        {
            Dispatcher.Invoke(() => _trayService?.ShowTip(args.title, args.message));
        };
        rec.ShowStoppingStateRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => _recordingStatusWindow?.ShowStoppingState());
        };
        rec.ShowSavingStateRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => _recordingStatusWindow?.ShowSavingState());
        };
        rec.ShowDiscardingStateRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => _recordingStatusWindow?.ShowDiscardingState());
        };
        rec.ShowRecordingStateRequested += (_, status) =>
        {
            Dispatcher.Invoke(() => _recordingStatusWindow?.ShowRecordingState(status));
        };
        rec.ShowPausedStateRequested += (_, _) =>
        {
            Dispatcher.Invoke(() => _recordingStatusWindow?.ShowPausedState());
        };
        rec.EnsureSettingsAppliedRequested += (_, args) =>
        {
            Dispatcher.Invoke(() => args.IsValid = ApplySettingsFromUi());
        };
        rec.PrerequisiteWarningRequested += (_, args) =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateSettingsHints();
                System.Windows.MessageBox.Show(args.Message, "缺少 FFmpeg", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.ShouldAbort = true;
            });
        };
        rec.RecordingSaved += (_, record) =>
        {
            Dispatcher.Invoke(() => _viewModel.History.AddRecordCommand.Execute(record));
        };

        // Keep RecordingButton state in sync with VM properties
        rec.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordingViewModel.ButtonLabel))
            {
                Dispatcher.Invoke(() => RecordingButtonLabel.Text = rec.ButtonLabel);
            }
        };
    }

    /// <summary>
    /// Opens or brings forward the RecordingStatusWindow for the decision state.
    /// </summary>
    private void ShowRecordingDecision(string fileName, string saveDirectory)
    {
        if (_recordingStatusWindow is null)
        {
            var pendingWindow = new RecordingStatusWindow(DateTimeOffset.Now, "请选择保存或取消");
            WireRecordingStatusWindowEvents(pendingWindow);
            _recordingStatusWindow = pendingWindow;
            pendingWindow.Show();
        }

        _recordingStatusWindow.ShowDecisionState(fileName, saveDirectory);
    }

    private void ShowRecordingStatusWindow(DateTimeOffset startedAt, string subtitle)
    {
        CloseRecordingStatusWindow();

        var window = new RecordingStatusWindow(startedAt, subtitle);
        WireRecordingStatusWindowEvents(window);
        _recordingStatusWindow = window;
        window.Show();
    }

    private void WireRecordingStatusWindowEvents(RecordingStatusWindow window)
    {
        window.StopRequested += (_, _) =>
            _ = _viewModel.Recording.ToggleRecordingCommand.ExecuteAsync(null);
        window.PauseRequested += (_, _) =>
            _ = _viewModel.Recording.TogglePauseRecordingCommand.ExecuteAsync(null);
        window.SaveRequested += (_, _) =>
            _ = _viewModel.Recording.SavePendingRecordingCommand.ExecuteAsync(null);
        window.CancelRequested += (_, _) =>
            _ = _viewModel.Recording.CancelPendingRecordingCommand.ExecuteAsync(null);
    }

    private void CloseRecordingStatusWindow()
    {
        if (_recordingStatusWindow is null)
        {
            return;
        }

        _recordingStatusWindow.Close();
        _recordingStatusWindow = null;
    }

    private bool ApplySettingsFromUi()
    {
        try
        {
            _settings.ScreenshotDirectory = ScreenshotDirectoryTextBox.Text.Trim();
            _settings.VideoDirectory = VideoDirectoryTextBox.Text.Trim();
            _settings.SaveDirectory = _settings.ScreenshotDirectory;
            _settings.EnableWatermark = WatermarkCheckBox.IsChecked == true;
            _settings.WatermarkTemplate = WatermarkTextBox.Text.Trim();
            if (RecordingModeComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                _settings.RecordingPerformanceMode = item.Tag?.ToString() ?? RecordingPerformanceProfiles.Smooth;
            }
            else
            {
                _settings.RecordingPerformanceMode = RecordingPerformanceProfiles.Smooth;
            }

            _settings.PreferFfmpegRecording = PreferFfmpegCheckBox.IsChecked == true;
            _settings.AllowLocalAviFallback = AllowLocalAviFallbackCheckBox.IsChecked == true;
            _settings.FfmpegPath = FfmpegPathTextBox.Text.Trim();
            _settings.RecordingCaptureSystemAudio = CaptureSystemAudioCheckBox.IsChecked == true;
            _settings.RecordingSystemAudioDevice = NormalizeAudioDeviceText(SystemAudioDeviceComboBox.Text);
            _settings.RecordingCaptureMicrophone = CaptureMicrophoneCheckBox.IsChecked == true;
            _settings.RecordingMicrophoneDevice = NormalizeAudioDeviceText(MicrophoneDeviceComboBox.Text);
            _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
            _settings.MinimizeToTrayOnClose = MinimizeToTrayOnCloseCheckBox.IsChecked == true;
            _settings.HistoryRetentionDays = GetSelectedHistoryRetentionDays();
            _settings.CaptureDelaySeconds = GetSelectedCaptureDelaySeconds();
            if (_settings.EnableWatermark && string.IsNullOrWhiteSpace(_settings.WatermarkTemplate))
            {
                _settings.WatermarkTemplate = "SnipEasy · {Timestamp}";
                WatermarkTextBox.Text = _settings.WatermarkTemplate;
            }

            if (!TryReadIntInRange(FrameRateTextBox.Text, 1, 30, "录屏帧率", out var frameRate))
            {
                return false;
            }

            if (!TryReadIntInRange(CrfTextBox.Text, 18, 35, "画质参数 CRF", out var crf))
            {
                return false;
            }

            _settings.RecordingFrameRate = frameRate;
            _settings.RecordingCrf = crf;
            UpdateSettingsHints();

            _ = ScreenCaptureService.ResolveScreenshotDirectory(_settings);
            _ = ScreenCaptureService.ResolveRecordingDirectory(_settings);
            UpdateRecordingModeDescription();
            UpdateDefaultPathHints();
            UpdateEngineStatus();
            return true;
        }
        catch (Exception ex)
        {
            RefreshStatus($"设置无效：{ex.Message}");
            return false;
        }
    }

    private void ApplySettingsToUi()
    {
        _isApplyingSettingsToUi = true;
        ScreenshotDirectoryTextBox.Text = _settings.ScreenshotDirectory;
        VideoDirectoryTextBox.Text = _settings.VideoDirectory;
        WatermarkCheckBox.IsChecked = _settings.EnableWatermark;
        WatermarkTextBox.Text = _settings.WatermarkTemplate;
        SelectRecordingMode(_settings.RecordingPerformanceMode);
        PreferFfmpegCheckBox.IsChecked = _settings.PreferFfmpegRecording;
        AllowLocalAviFallbackCheckBox.IsChecked = _settings.AllowLocalAviFallback;
        FfmpegPathTextBox.Text = _settings.FfmpegPath;
        CaptureSystemAudioCheckBox.IsChecked = _settings.RecordingCaptureSystemAudio;
        SystemAudioDeviceComboBox.Text = _settings.RecordingSystemAudioDevice;
        CaptureMicrophoneCheckBox.IsChecked = _settings.RecordingCaptureMicrophone;
        MicrophoneDeviceComboBox.Text = _settings.RecordingMicrophoneDevice;
        UpdateAudioDeviceSelectors([]);
        StartWithWindowsCheckBox.IsChecked = _startupService.IsEnabled();
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        MinimizeToTrayOnCloseCheckBox.IsChecked = _settings.MinimizeToTrayOnClose;
        SelectHistoryRetentionDays(_settings.HistoryRetentionDays);
        SelectCaptureDelaySeconds(_settings.CaptureDelaySeconds);
        FrameRateTextBox.Text = _settings.RecordingFrameRate.ToString();
        CrfTextBox.Text = _settings.RecordingCrf.ToString();

        // 加载快捷键配置
        ScreenshotHotkeyDisplay.Text = _settings.Hotkeys.Screenshot;
        RecordingHotkeyDisplay.Text = _settings.Hotkeys.Recording;
        StickerHotkeyDisplay.Text = _settings.Hotkeys.Sticker;
        ColorPickerHotkeyDisplay.Text = _settings.Hotkeys.ColorPicker;

        _isApplyingSettingsToUi = false;
        UpdateSettingsHints();
        UpdateRecordingModeDescription();
        UpdateDefaultPathHints();
        UpdateEngineStatus();
    }

    private void SelectRecordingMode(string mode)
    {
        var normalized = RecordingPerformanceProfiles.NormalizeMode(mode);
        foreach (var item in RecordingModeComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                RecordingModeComboBox.SelectedItem = item;
                return;
            }
        }

        RecordingModeComboBox.SelectedIndex = 0;
    }

    private int GetSelectedHistoryRetentionDays()
    {
        if (HistoryRetentionComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var days))
        {
            return days;
        }

        return 90;
    }

    private void SelectHistoryRetentionDays(int days)
    {
        var normalized = days <= 0 ? 0 : days <= 30 ? 30 : days <= 90 ? 90 : days <= 180 ? 180 : 365;
        foreach (var item in HistoryRetentionComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var itemDays) && itemDays == normalized)
            {
                HistoryRetentionComboBox.SelectedItem = item;
                return;
            }
        }

        HistoryRetentionComboBox.SelectedIndex = 1;
    }

    private int GetSelectedCaptureDelaySeconds()
    {
        if (CaptureDelayComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item &&
            int.TryParse(item.Tag?.ToString(), out var seconds))
        {
            return Math.Clamp(seconds, 0, 10);
        }

        return 0;
    }

    private void SelectCaptureDelaySeconds(int seconds)
    {
        var normalized = seconds switch
        {
            >= 10 => 10,
            >= 5 => 5,
            >= 3 => 3,
            _ => 0
        };

        foreach (var item in CaptureDelayComboBox.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (int.TryParse(item.Tag?.ToString(), out var itemSeconds) && itemSeconds == normalized)
            {
                CaptureDelayComboBox.SelectedItem = item;
                return;
            }
        }

        CaptureDelayComboBox.SelectedIndex = 0;
    }

    private bool TryReadIntInRange(string text, int minimum, int maximum, string label, out int value)
    {
        if (!int.TryParse(text.Trim(), out value) || value < minimum || value > maximum)
        {
            var message = $"{label} 必须是 {minimum}-{maximum} 之间的数字。";
            RecordingParameterHintText.Text = message;
            RecordingParameterHintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
            RefreshStatus(message);
            return false;
        }

        return true;
    }

    private void UpdateSettingsHints()
    {
        if (RecordingParameterHintText is not null)
        {
            RecordingParameterHintText.Text = $"可用范围：帧率 1-30 FPS，CRF 18-35；数值越小画质越高。";
            RecordingParameterHintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 112, 133));
        }

        if (FfmpegPathHintText is null)
        {
            return;
        }

        var ffmpegPath = FfmpegPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            FfmpegPathHintText.Text = $"未安装 FFmpeg 时只能无声音兼容录屏；如需录电脑声音，请把 ffmpeg.exe 放到：{FfmpegRecordingService.GetBundledFfmpegPath()}。";
            FfmpegPathHintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9));
            return;
        }

        if (File.Exists(ffmpegPath))
        {
            FfmpegPathHintText.Text = "已找到指定的 ffmpeg.exe。";
            FfmpegPathHintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(4, 120, 87));
            return;
        }

        FfmpegPathHintText.Text = "未找到这个 ffmpeg.exe；请检查路径，或留空让 SnipEasy 自动查找。";
        FfmpegPathHintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9));
    }

    private void UpdateRecordingModeDescription()
    {
        var profile = RecordingPerformanceProfiles.Resolve(_settings.RecordingPerformanceMode);
        RecordingModeDescriptionText.Text = $"{profile.Description} 当前建议参数：{profile.FrameRate} FPS / CRF {profile.Crf}";
    }

    private void HistorySearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _viewModel.History.SearchQuery = HistorySearchTextBox?.Text ?? "";
    }

    private void HistoryKindFilterComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _viewModel.History.SelectedKindFilter = GetSelectedHistoryKind();
    }

    private CaptureKind? GetSelectedHistoryKind()
    {
        if (HistoryKindFilterComboBox?.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        {
            return null;
        }

        return item.Tag?.ToString() switch
        {
            "Screenshot" => CaptureKind.Screenshot,
            "Recording" => CaptureKind.Recording,
            _ => null
        };
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
        ApplySettingsToUi();
    }

    private void UpdateDefaultPathHints()
    {
        ScreenshotDefaultText.Text = $"留空默认：{_paths.DefaultScreenshotDirectory}";
        VideoDefaultText.Text = $"留空默认：{_paths.DefaultVideoDirectory}";
    }

    private void UpdateEngineStatus()
    {
        var ffmpegPath = _recordingService.ResolveFfmpegPath(_settings);
        var audioRoute = BuildAudioRouteLabel();
        if (_settings.PreferFfmpegRecording && !string.IsNullOrWhiteSpace(ffmpegPath))
        {
            var profile = RecordingPerformanceProfiles.Resolve(_settings.RecordingPerformanceMode);
            EngineStatusText.Text = $"截图：区域批注可用 | 录屏：MP4 {audioRoute} · {profile.DisplayName}";
            EngineStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(4, 120, 87));
        }
        else if (_settings.AllowLocalAviFallback)
        {
            EngineStatusText.Text = "截图：区域批注可用 | 录屏：AVI 兼容模式（无音频）；安装 FFmpeg 后可录电脑声音";
            EngineStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(180, 83, 9));
        }
        else
        {
            EngineStatusText.Text = "截图：区域批注可用 | 录屏：等待 FFmpeg 组件";
            EngineStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185, 28, 28));
        }

        FooterText.Text = $"配置：{_paths.SettingsPath}";
    }

    private string BuildAudioRouteLabel()
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

        return routes.Count == 0 ? "无音频" : string.Join("+", routes);
    }

    private void RefreshStatus(string message)
    {
        _viewModel.StatusText = message;
        _logger.Info($"Status: {message}");
    }

    private void OpenSaveDirectory()
    {
        if (!ApplySettingsFromUi())
        {
            return;
        }

        OpenDirectory(ScreenCaptureService.ResolveScreenshotDirectory(_settings));
    }

    private static void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{directory}\"",
            UseShellExecute = true
        });
    }

    private void BrowseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = "选择 ffmpeg.exe",
            Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            FfmpegPathTextBox.Text = dialog.FileName;
            _ = ApplySettingsFromUi();
            SaveSettings();
        }
    }

    private void AutoDetectFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        var originalPath = FfmpegPathTextBox.Text;
        FfmpegPathTextBox.Text = "";
        _settings.FfmpegPath = "";
        var detected = _recordingService.ResolveFfmpegPath(_settings);
        if (string.IsNullOrWhiteSpace(detected))
        {
            FfmpegPathTextBox.Text = originalPath;
            UpdateSettingsHints();
            RefreshStatus($"未检测到 FFmpeg。请把 ffmpeg.exe 放到：{FfmpegRecordingService.GetBundledFfmpegPath()}");
            return;
        }

        FfmpegPathTextBox.Text = detected;
        _ = ApplySettingsFromUi();
        SaveSettings();
        RefreshStatus($"已检测到 FFmpeg：{detected}");
    }

    private void OpenFfmpegFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenDirectory(FfmpegRecordingService.GetBundledFfmpegDirectory());
        RefreshStatus($"已打开组件目录；请把 ffmpeg.exe 放到这里：{FfmpegRecordingService.GetBundledFfmpegPath()}");
    }

    private async void DiagnoseFfmpeg_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplySettingsFromUi())
        {
            return;
        }

        RefreshStatus("正在执行 FFmpeg 自检...");
        try
        {
            var result = await _recordingService.DiagnoseFfmpegAsync(_settings);
            UpdateAudioDeviceSelectors(result.AudioDevices);
            var warningsText = result.Warnings is { Count: > 0 }
                ? $"{Environment.NewLine}{Environment.NewLine}注意事项：{Environment.NewLine}{string.Join(Environment.NewLine, result.Warnings.Select(warning => $"- {warning}"))}"
                : "";
            var message = result.IsAvailable
                ? $"{result.Message}{Environment.NewLine}{Environment.NewLine}路径：{result.Path}{Environment.NewLine}版本：{result.Version}{Environment.NewLine}{Environment.NewLine}音频设备：{Environment.NewLine}{string.Join(Environment.NewLine, result.AudioDevices.DefaultIfEmpty("未发现"))}{warningsText}"
                : result.Message;

            System.Windows.MessageBox.Show(message, "FFmpeg 自检", MessageBoxButton.OK, result.IsAvailable ? MessageBoxImage.Information : MessageBoxImage.Warning);
            RefreshStatus(result.Message);
        }
        catch (Exception ex)
        {
            _logger.Error("Diagnose FFmpeg failed.", ex);
            RefreshStatus($"FFmpeg 自检失败：{ex.Message}");
        }
    }

    private async void ListAudioDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAudioDevicesAsync(showStatus: true, force: true);
    }

    private async void AudioDeviceComboBox_DropDownOpened(object sender, EventArgs e)
    {
        await RefreshAudioDevicesAsync(showStatus: true, force: false);
    }

    private async Task RefreshAudioDevicesAsync(bool showStatus, bool force)
    {
        if (_audioDeviceRefreshBusy || (!force && _audioDeviceRefreshAttempted))
        {
            return;
        }

        if (!ApplySettingsFromUi())
        {
            return;
        }

        _audioDeviceRefreshBusy = true;
        if (showStatus)
        {
            RefreshStatus("正在刷新 FFmpeg 音频设备...");
        }

        try
        {
            var result = await _recordingService.DiagnoseFfmpegAsync(_settings);
            _audioDeviceRefreshAttempted = true;
            UpdateAudioDeviceSelectors(result.AudioDevices);
            RefreshStatus(result.AudioDevices.Count == 0 ? result.Message : $"已刷新 {result.AudioDevices.Count} 个音频设备。可在下拉框中选择。");
        }
        catch (Exception ex)
        {
            _audioDeviceRefreshAttempted = true;
            _logger.Error("List audio devices failed.", ex);
            UpdateAudioDeviceSelectors([]);
            RefreshStatus($"枚举音频设备失败：{ex.Message}");
        }
        finally
        {
            _audioDeviceRefreshBusy = false;
        }
    }

    private void UpdateAudioDeviceSelectors(IReadOnlyList<string> devices)
    {
        var systemValue = NormalizeAudioDeviceText(SystemAudioDeviceComboBox.Text);
        var microphoneValue = NormalizeAudioDeviceText(MicrophoneDeviceComboBox.Text);
        var items = devices.Count == 0
            ? [AudioDeviceManualEntryHint]
            : devices.ToList();

        SystemAudioDeviceComboBox.ItemsSource = items;
        MicrophoneDeviceComboBox.ItemsSource = items;
        SystemAudioDeviceComboBox.Text = string.IsNullOrWhiteSpace(systemValue) ? _settings.RecordingSystemAudioDevice : systemValue;
        MicrophoneDeviceComboBox.Text = string.IsNullOrWhiteSpace(microphoneValue) ? _settings.RecordingMicrophoneDevice : microphoneValue;
    }

    private static string NormalizeAudioDeviceText(string value)
    {
        var text = value.Trim();
        return string.Equals(text, AudioDeviceManualEntryHint, StringComparison.OrdinalIgnoreCase) ? "" : text;
    }

    private void ExportHistoryCsv_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.History.ExportCsvCommand.Execute(null);
    }

    private void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = "导出 SnipEasy 诊断包",
            Filter = "ZIP 文件|*.zip|所有文件|*.*",
            FileName = $"SnipEasy-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip"
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            _diagnosticPackageService.Export(dialog.FileName, _recordingService.ResolveFfmpegPath(_settings), EngineStatusText.Text);
            RefreshStatus($"诊断包已导出：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            _logger.Error("Export diagnostics failed.", ex);
            RefreshStatus($"导出诊断包失败：{ex.Message}");
        }
    }

    private string? _recordingHotkeyType;

    private void ScreenshotHotkeyTextBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StartHotkeyRecording("Screenshot");
    }

    private void RecordingHotkeyTextBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StartHotkeyRecording("Recording");
    }

    private void ColorPickerHotkeyTextBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StartHotkeyRecording("ColorPicker");
    }

    private void StickerHotkeyTextBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        StartHotkeyRecording("Sticker");
    }

    private void StartHotkeyRecording(string hotkeyType)
    {
        // 取消之前的录制
        CancelHotkeyRecording();

        _recordingHotkeyType = hotkeyType;

        // 临时注销全局热键，避免拦截按键
        _hotkeyManager?.Unregister(ScreenshotHotkeyId);
        _hotkeyManager?.Unregister(RecordingHotkeyId);
        _hotkeyManager?.Unregister(StickerHotkeyId);
        _hotkeyManager?.Unregister(ColorPickerHotkeyId);

        // 更新 UI
        var display = GetHotkeyDisplay(hotkeyType);
        var border = GetHotkeyBorder(hotkeyType);
        if (display is not null)
        {
            display.Text = "按下新快捷键...";
            display.Foreground = System.Windows.Media.Brushes.Red;
        }
        if (border is not null)
        {
            border.BorderBrush = System.Windows.Media.Brushes.Red;
        }

        // 确保窗口获得键盘焦点
        Focus();
        Keyboard.Focus(this);
    }

    private void CancelHotkeyRecording()
    {
        if (_recordingHotkeyType is null)
        {
            return;
        }

        var hotkeyType = _recordingHotkeyType;
        _recordingHotkeyType = null;

        // 恢复 UI
        var display = GetHotkeyDisplay(hotkeyType);
        var border = GetHotkeyBorder(hotkeyType);
        if (display is not null)
        {
            display.Text = GetCurrentHotkeyValue(hotkeyType);
            display.Foreground = System.Windows.Media.Brushes.Black;
        }
        if (border is not null)
        {
            border.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderDarkBrush");
        }

        // 恢复全局热键
        RegisterHotkeys();
    }

    private System.Windows.Controls.TextBlock? GetHotkeyDisplay(string hotkeyType) => hotkeyType switch
    {
        "Screenshot" => ScreenshotHotkeyDisplay,
        "Recording" => RecordingHotkeyDisplay,
        "Sticker" => StickerHotkeyDisplay,
        "ColorPicker" => ColorPickerHotkeyDisplay,
        _ => null
    };

    private System.Windows.Controls.Border? GetHotkeyBorder(string hotkeyType) => hotkeyType switch
    {
        "Screenshot" => ScreenshotHotkeyBorder,
        "Recording" => RecordingHotkeyBorder,
        "Sticker" => StickerHotkeyBorder,
        "ColorPicker" => ColorPickerHotkeyBorder,
        _ => null
    };

    private string GetCurrentHotkeyValue(string hotkeyType) => hotkeyType switch
    {
        "Screenshot" => _settings.Hotkeys.Screenshot,
        "Recording" => _settings.Hotkeys.Recording,
        "Sticker" => _settings.Hotkeys.Sticker,
        "ColorPicker" => _settings.Hotkeys.ColorPicker,
        _ => ""
    };

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Esc 取消录制
        if (e.Key == Key.Escape && _recordingHotkeyType is not null)
        {
            CancelHotkeyRecording();
            e.Handled = true;
            return;
        }

        // 检查是否正在录制
        if (_recordingHotkeyType is null)
        {
            return;
        }

        e.Handled = true;

        // 忽略单独的修饰键
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            return;
        }

        // 构建快捷键字符串
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

        var keyName = e.Key switch
        {
            Key.F1 => "F1", Key.F2 => "F2", Key.F3 => "F3", Key.F4 => "F4",
            Key.F5 => "F5", Key.F6 => "F6", Key.F7 => "F7", Key.F8 => "F8",
            Key.F9 => "F9", Key.F10 => "F10", Key.F11 => "F11", Key.F12 => "F12",
            Key.Space => "Space", Key.Enter => "Enter", Key.Escape => "Escape",
            Key.Tab => "Tab", Key.Back => "Backspace", Key.Delete => "Delete",
            _ => e.Key.ToString()
        };

        parts.Add(keyName);
        var hotkey = string.Join("+", parts);

        // 更新设置
        var hotkeyType = _recordingHotkeyType;
        switch (hotkeyType)
        {
            case "Screenshot": _settings.Hotkeys.Screenshot = hotkey; break;
            case "Recording": _settings.Hotkeys.Recording = hotkey; break;
            case "ColorPicker": _settings.Hotkeys.ColorPicker = hotkey; break;
            case "Sticker": _settings.Hotkeys.Sticker = hotkey; break;
        }

        _recordingHotkeyType = null;

        // 更新 UI
        var display = GetHotkeyDisplay(hotkeyType!);
        var border = GetHotkeyBorder(hotkeyType!);
        if (display is not null)
        {
            display.Text = hotkey;
            display.Foreground = System.Windows.Media.Brushes.Black;
        }
        if (border is not null)
        {
            border.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderDarkBrush");
        }

        // 保存设置并更新热键
        _settingsService.Save(_settings);
        RegisterHotkeys();
        RefreshStatus($"快捷键已更新：{hotkeyType} = {hotkey}");
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _settings.Hotkeys.Screenshot = "F1";
        _settings.Hotkeys.Recording = "F2";
        _settings.Hotkeys.Sticker = "F3";
        _settings.Hotkeys.ColorPicker = "F4";

        ScreenshotHotkeyDisplay.Text = "F1";
        RecordingHotkeyDisplay.Text = "F2";
        StickerHotkeyDisplay.Text = "F3";
        ColorPickerHotkeyDisplay.Text = "F4";

        _settingsService.Save(_settings);
        UpdateHotkeys();
        RefreshStatus("快捷键已恢复为默认配置。");
    }

    private void OpenSelectedFolder_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CaptureRecord record || string.IsNullOrWhiteSpace(record.FilePath))
        {
            RefreshStatus("请先选择一条历史记录。");
            return;
        }

        var directory = Path.GetDirectoryName(record.FilePath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            RefreshStatus("所选记录的文件夹不存在。");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{record.FilePath}\"",
            UseShellExecute = true
        });
    }

    private void OpenSelectedFile()
    {
        if (HistoryGrid.SelectedItem is not CaptureRecord record || !File.Exists(record.FilePath))
        {
            RefreshStatus("请先选择一条存在的历史记录。");
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = record.FilePath,
            UseShellExecute = true
        });
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose && _settings.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            _trayService?.ShowTip("SnipEasy 仍在运行", "F1/F2 快捷键继续生效。");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _viewModel.Recording.DiscardPendingDraft();
        _stickerManager.SaveState();
        CloseRecordingStatusWindow();
        _hotkeyManager?.Dispose();
        _trayService?.Dispose();
        _recordingService.Dispose();
    }
}
