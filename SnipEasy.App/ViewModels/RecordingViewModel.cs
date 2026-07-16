using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.ViewModels;

/// <summary>
/// Event arguments for requesting settings validation from the View.
/// </summary>
public sealed class SettingsValidationEventArgs : EventArgs
{
    public bool IsValid { get; set; }
}

/// <summary>
/// Event arguments for requesting a prerequisite warning from the View.
/// </summary>
public sealed class PrerequisiteWarningEventArgs : EventArgs
{
    public required string Message { get; init; }
    public bool ShouldAbort { get; set; }
}

/// <summary>
/// ViewModel for recording operations.
/// Manages recording lifecycle, state, and user decisions.
/// </summary>
public partial class RecordingViewModel : ObservableObject
{
    private readonly AppLogger _logger;
    private readonly IRecordingService _recordingService;
    private readonly RecordingDraftService _recordingDraftService;
    private readonly ClipboardService _clipboardService;
    private readonly AppSettings _settings;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private bool _isStopping;

    [ObservableProperty]
    private bool _hasPendingDecision;

    [ObservableProperty]
    private string _buttonLabel = "开始录屏";

    [ObservableProperty]
    private string _statusSubtitle = string.Empty;

    [ObservableProperty]
    private string _pendingFileName = string.Empty;

    [ObservableProperty]
    private string _pendingSaveDirectory = string.Empty;

    private CaptureRecord? _pendingRecord;
    private string _pendingRecordingSaveDirectory = "";
    private bool _commandBusy;
    private bool _decisionBusy;

    public RecordingViewModel(
        AppLogger logger,
        IRecordingService recordingService,
        RecordingDraftService recordingDraftService,
        ClipboardService clipboardService,
        AppSettings settings)
    {
        _logger = logger;
        _recordingService = recordingService;
        _recordingDraftService = recordingDraftService;
        _clipboardService = clipboardService;
        _settings = settings;
    }

    /// <summary>
    /// Event raised when a recording status window should be shown.
    /// </summary>
    public event EventHandler<(DateTimeOffset startedAt, string subtitle)>? ShowRecordingStatusRequested;

    /// <summary>
    /// Event raised when the recording status window should be closed.
    /// </summary>
    public event EventHandler? CloseRecordingStatusRequested;

    /// <summary>
    /// Event raised when a pending recording decision should be shown.
    /// </summary>
    public event EventHandler<(string fileName, string saveDirectory)>? ShowDecisionRequested;

    /// <summary>
    /// Event raised when status text should be refreshed.
    /// </summary>
    public event EventHandler<string>? StatusRefreshRequested;

    /// <summary>
    /// Event raised when a tray tip should be shown.
    /// </summary>
    public event EventHandler<(string title, string message)>? TrayTipRequested;

    /// <summary>
    /// Event raised when the recording status window should show the stopping state.
    /// </summary>
    public event EventHandler? ShowStoppingStateRequested;

    /// <summary>
    /// Event raised when the recording status window should show the saving state.
    /// </summary>
    public event EventHandler? ShowSavingStateRequested;

    /// <summary>
    /// Event raised when the recording status window should show the discarding state.
    /// </summary>
    public event EventHandler? ShowDiscardingStateRequested;

    /// <summary>
    /// Event raised when the recording status window should show the recording state with an optional status message.
    /// </summary>
    public event EventHandler<string?>? ShowRecordingStateRequested;

    /// <summary>
    /// Event raised when the recording status window should show the paused state.
    /// </summary>
    public event EventHandler? ShowPausedStateRequested;

    /// <summary>
    /// Event raised when settings need to be validated/applied from the UI before recording.
    /// The handler sets <see cref="SettingsValidationEventArgs.IsValid"/> to indicate result.
    /// </summary>
    public event EventHandler<SettingsValidationEventArgs>? EnsureSettingsAppliedRequested;

    /// <summary>
    /// Event raised when a prerequisite warning should be shown to the user.
    /// The handler sets <see cref="PrerequisiteWarningEventArgs.ShouldAbort"/> to true if recording should not proceed.
    /// </summary>
    public event EventHandler<PrerequisiteWarningEventArgs>? PrerequisiteWarningRequested;

    /// <summary>
    /// Event raised when a recording has been saved and should be added to history.
    /// </summary>
    public event EventHandler<CaptureRecord>? RecordingSaved;

    /// <summary>
    /// Gets whether a recording command is currently in progress.
    /// </summary>
    public bool IsCommandBusy => _commandBusy;

    /// <summary>
    /// Gets whether a decision command is currently in progress.
    /// </summary>
    public bool IsDecisionBusy => _decisionBusy;

    [RelayCommand]
    private async System.Threading.Tasks.Task ToggleRecordingAsync()
    {
        if (_recordingService.IsRecording)
        {
            await StopRecordingAsync();
        }
        else if (HasPendingDecision)
        {
            ShowPendingDecision();
            StatusRefreshRequested?.Invoke(this, "请先保存或取消上一段录屏。");
        }
        else
        {
            await RequestStartRecordingAsync();
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task TogglePauseRecordingAsync()
    {
        if (_commandBusy || !_recordingService.IsRecording)
        {
            return;
        }

        _commandBusy = true;
        try
        {
            if (_recordingService.IsPaused)
            {
                await _recordingService.ResumeAsync();
                IsPaused = false;
                ShowRecordingStateRequested?.Invoke(this, null);
                StatusRefreshRequested?.Invoke(this, "录屏已继续。");
                _logger.Info("Recording resumed by user.");
            }
            else
            {
                await _recordingService.PauseAsync();
                IsPaused = true;
                ShowPausedStateRequested?.Invoke(this, EventArgs.Empty);
                StatusRefreshRequested?.Invoke(this, "录屏已暂停。");
                _logger.Info("Recording paused by user.");
            }
        }
        catch (Exception ex)
        {
            IsPaused = _recordingService.IsPaused;
            if (IsPaused)
            {
                ShowPausedStateRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ShowRecordingStateRequested?.Invoke(this, "暂停操作失败，仍在录屏");
            }

            _logger.Error("Recording pause/resume failed.", ex);
            StatusRefreshRequested?.Invoke(this, $"录屏暂停操作失败：{ex.Message}");
        }
        finally
        {
            _commandBusy = false;
            UpdateButtonLabel();
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task SavePendingRecordingAsync()
    {
        if (_decisionBusy || _pendingRecord is null)
        {
            if (_pendingRecord is null)
            {
                CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
                UpdateButtonLabel();
            }
            return;
        }

        _decisionBusy = true;
        ShowSavingStateRequested?.Invoke(this, EventArgs.Empty);

        try
        {
            var sourcePath = _pendingRecord.FilePath;
            if (!System.IO.File.Exists(sourcePath))
            {
                throw new System.IO.FileNotFoundException("录屏草稿文件不存在。", sourcePath);
            }

            var saveDirectory = string.IsNullOrWhiteSpace(_pendingRecordingSaveDirectory)
                ? ScreenCaptureService.ResolveRecordingDirectory(_settings)
                : _pendingRecordingSaveDirectory;
            System.IO.Directory.CreateDirectory(saveDirectory);

            var finalPath = _recordingDraftService.CreateUniqueFilePath(saveDirectory, System.IO.Path.GetFileName(sourcePath));
            if (!string.Equals(sourcePath, finalPath, StringComparison.OrdinalIgnoreCase))
            {
                await System.Threading.Tasks.Task.Run(() => _recordingDraftService.MoveDraft(sourcePath, finalPath));
            }

            _pendingRecord.FilePath = finalPath;
            _pendingRecord.FileSizeBytes = new System.IO.FileInfo(finalPath).Length;
            _pendingRecord.ClipboardMode = "File";
            _clipboardService.SetFileDrop(_pendingRecord.FilePath);
            RecordingSaved?.Invoke(this, _pendingRecord);

            _logger.Info($"Recording saved by user: {_pendingRecord.FilePath}");
            StatusRefreshRequested?.Invoke(this, $"录屏已保存并复制为文件：{System.IO.Path.GetFileName(_pendingRecord.FilePath)}");
            TrayTipRequested?.Invoke(this, ("录屏已保存", $"视频已保存到：{System.IO.Path.GetDirectoryName(_pendingRecord.FilePath)}"));

            ClearPendingState();
            CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("Save pending recording failed.", ex);
            StatusRefreshRequested?.Invoke(this, $"录屏保存失败：{ex.Message}");
            ShowPendingDecision();
        }
        finally
        {
            _decisionBusy = false;
            UpdateButtonLabel();
        }
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CancelPendingRecordingAsync()
    {
        if (_decisionBusy || _pendingRecord is null)
        {
            if (_pendingRecord is null)
            {
                CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
                UpdateButtonLabel();
            }
            return;
        }

        _decisionBusy = true;
        ShowDiscardingStateRequested?.Invoke(this, EventArgs.Empty);

        try
        {
            await System.Threading.Tasks.Task.Run(() => _recordingDraftService.DeleteFileIfExists(_pendingRecord.FilePath));
            _logger.Info($"Recording discarded by user: {_pendingRecord.FilePath}");
            StatusRefreshRequested?.Invoke(this, "已取消并删除本次录屏。");
            TrayTipRequested?.Invoke(this, ("录屏已取消", "本次录屏没有保存。"));

            ClearPendingState();
            CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.Error("Cancel pending recording failed.", ex);
            StatusRefreshRequested?.Invoke(this, $"取消录屏失败：{ex.Message}");
            ShowPendingDecision();
        }
        finally
        {
            _decisionBusy = false;
            UpdateButtonLabel();
        }
    }

    /// <summary>
    /// Discards any pending recording draft (called during shutdown).
    /// </summary>
    public void DiscardPendingDraft()
    {
        if (_pendingRecord is null)
        {
            return;
        }

        try
        {
            _recordingDraftService.DeleteFileIfExists(_pendingRecord.FilePath);
            _logger.Info($"Pending recording draft discarded during shutdown: {_pendingRecord.FilePath}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to delete pending recording draft: {ex.Message}");
        }

        ClearPendingState();
    }

    private async System.Threading.Tasks.Task RequestStartRecordingAsync()
    {
        if (_commandBusy)
        {
            _logger.Info("Recording start request ignored because another recording command is running.");
            return;
        }

        _commandBusy = true;
        try
        {
            var settingsArgs = new SettingsValidationEventArgs();
            EnsureSettingsAppliedRequested?.Invoke(this, settingsArgs);
            if (!settingsArgs.IsValid)
            {
                _logger.Warn("Recording request ignored because settings are invalid.");
                return;
            }

            if (!EnsurePrerequisites())
            {
                return;
            }

            await StartRecordingAsync();
        }
        finally
        {
            _commandBusy = false;
            UpdateButtonLabel();
        }
    }

    private async System.Threading.Tasks.Task StartRecordingAsync()
    {
        var startedAt = DateTimeOffset.Now;
        try
        {
            _logger.Info("Recording start requested.");
            ClearPendingState();
            _pendingRecordingSaveDirectory = ScreenCaptureService.ResolveRecordingDirectory(_settings);

            var outputPath = await _recordingService.StartAsync(
                _recordingDraftService.CreateDraftSettings(_settings));

            IsRecording = true;
            IsPaused = false;
            UpdateButtonLabel();
            ShowRecordingStatusRequested?.Invoke(this, (startedAt, BuildStatusSubtitle()));

            _logger.Info($"Recording started: {outputPath}");
            var fallbackText = string.IsNullOrWhiteSpace(_recordingService.LastFallbackReason)
                ? ""
                : $" {_recordingService.LastFallbackReason}";
            StatusRefreshRequested?.Invoke(this, $"正在录屏：{System.IO.Path.GetFileName(outputPath)} | {_recordingService.EngineName}{fallbackText}");
            TrayTipRequested?.Invoke(this, ("录屏已开始", $"当前后端：{_recordingService.EngineName}"));
        }
        catch (Exception ex)
        {
            CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
            ClearPendingState();
            _logger.Error("Recording start failed.", ex);
            StatusRefreshRequested?.Invoke(this, $"录屏启动失败：{ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task StopRecordingAsync()
    {
        if (_commandBusy)
        {
            ShowStoppingStateRequested?.Invoke(this, EventArgs.Empty);
            _logger.Info("Recording stop request ignored because another recording command is running.");
            return;
        }

        _commandBusy = true;
        IsStopping = true;
        IsPaused = false;
        ShowStoppingStateRequested?.Invoke(this, EventArgs.Empty);
        _logger.Info("Recording stop requested.");
        StatusRefreshRequested?.Invoke(this, "正在停止录屏并写入文件...");

        try
        {
            var record = await _recordingService.StopAsync();
            _pendingRecord = record;
            _pendingRecordingSaveDirectory = string.IsNullOrWhiteSpace(_pendingRecordingSaveDirectory)
                ? ScreenCaptureService.ResolveRecordingDirectory(_settings)
                : _pendingRecordingSaveDirectory;

            IsRecording = false;
            IsPaused = false;
            HasPendingDecision = true;
            PendingFileName = System.IO.Path.GetFileName(record.FilePath);
            PendingSaveDirectory = _pendingRecordingSaveDirectory;

            _logger.Info($"Recording completed and waiting for user decision: {record.FilePath}");
            StatusRefreshRequested?.Invoke(this, $"录屏已完成：{PendingFileName}。请选择保存或取消。");
            TrayTipRequested?.Invoke(this, ("录屏已完成", "请选择保存或取消。"));
            ShowPendingDecision();
        }
        catch (Exception ex)
        {
            _logger.Error("Recording stop failed.", ex);
            StatusRefreshRequested?.Invoke(this, $"录屏停止失败：{ex.Message}");
            if (_recordingService.IsRecording)
            {
                IsRecording = true;
                IsPaused = _recordingService.IsPaused;
                if (IsPaused)
                {
                    ShowPausedStateRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    ShowRecordingStateRequested?.Invoke(this, "停止失败，仍在录屏");
                }
            }
            else
            {
                CloseRecordingStatusRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            IsStopping = false;
            _commandBusy = false;
            UpdateButtonLabel();
        }
    }

    private bool EnsurePrerequisites()
    {
        var wantsAudio = _settings.RecordingCaptureSystemAudio || _settings.RecordingCaptureMicrophone;
        if (!wantsAudio)
        {
            return true;
        }

        if (_recordingService.IsFfmpegAvailable(_settings))
        {
            return true;
        }

        var message = $"当前勾选了录制声音，但没有找到 FFmpeg，所以不能录电脑声音或麦克风。" +
            $"{System.Environment.NewLine}{System.Environment.NewLine}" +
            $"解决办法：把 ffmpeg.exe 放到：{FfmpegRecordingService.GetBundledFfmpegPath()}，" +
            $"或者点击\"选择\"指定已有的 ffmpeg.exe。" +
            $"{System.Environment.NewLine}{System.Environment.NewLine}" +
            $"如果只想无声音录屏，请先取消\"录制电脑声音/录制麦克风\"。";
        StatusRefreshRequested?.Invoke(this, "缺少 FFmpeg，无法录制声音。已在设置中显示放置路径。");

        var warningArgs = new PrerequisiteWarningEventArgs { Message = message };
        PrerequisiteWarningRequested?.Invoke(this, warningArgs);
        return !warningArgs.ShouldAbort;
    }

    private void ShowPendingDecision()
    {
        if (_pendingRecord is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_pendingRecordingSaveDirectory))
        {
            _pendingRecordingSaveDirectory = ScreenCaptureService.ResolveRecordingDirectory(_settings);
        }

        ShowDecisionRequested?.Invoke(this, (PendingFileName, _pendingRecordingSaveDirectory));
    }

    private void ClearPendingState()
    {
        _pendingRecord = null;
        _pendingRecordingSaveDirectory = "";
        HasPendingDecision = false;
        PendingFileName = string.Empty;
        PendingSaveDirectory = string.Empty;
    }

    private void UpdateButtonLabel()
    {
        ButtonLabel = IsRecording
            ? "停止录屏"
            : HasPendingDecision
                ? "处理录屏"
                : "开始录屏";
    }

    private string BuildStatusSubtitle()
    {
        if (_recordingService.EngineName.Contains("AVI", StringComparison.OrdinalIgnoreCase))
        {
            return "正在录制画面（无音频）";
        }

        var audioRoute = _settings.RecordingCaptureSystemAudio || _settings.RecordingCaptureMicrophone
            ? BuildAudioRouteLabel()
            : "无音频";
        return audioRoute == "无音频"
            ? "正在录制画面（无音频）"
            : $"正在录制：{audioRoute}";
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
}
