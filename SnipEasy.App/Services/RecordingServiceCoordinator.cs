using SnipEasy.App.Models;

namespace SnipEasy.App.Services;

public sealed class RecordingServiceCoordinator : IRecordingService
{
    private readonly AppLogger _logger;
    private readonly FfmpegRecordingService _ffmpegService;
    private readonly LocalAviRecordingService _aviService = new();
    private IRecordingService? _activeService;

    public RecordingServiceCoordinator(AppLogger logger)
    {
        _logger = logger;
        _ffmpegService = new FfmpegRecordingService(logger);
    }

    public bool IsRecording => _activeService?.IsRecording == true;
    public string EngineName => _activeService?.EngineName ?? GetPreferredEngineName();
    public string LastFallbackReason { get; private set; } = "";

    public bool IsFfmpegAvailable(AppSettings settings)
    {
        return FfmpegRecordingService.IsAvailable(settings);
    }

    public string ResolveFfmpegPath(AppSettings settings)
    {
        return FfmpegRecordingService.ResolveFfmpegPath(settings);
    }

    public Task<string> ListAudioDevicesAsync(AppSettings settings)
    {
        return _ffmpegService.ListDirectShowDevicesAsync(settings);
    }

    public Task<FfmpegDiagnosticResult> DiagnoseFfmpegAsync(AppSettings settings)
    {
        return _ffmpegService.DiagnoseAsync(settings);
    }

    public async Task<string> StartAsync(AppSettings settings)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("录屏已经在进行中。");
        }

        LastFallbackReason = "";

        if (settings.PreferFfmpegRecording)
        {
            if (FfmpegRecordingService.IsAvailable(settings))
            {
                try
                {
                    _activeService = _ffmpegService;
                    return await _ffmpegService.StartAsync(settings).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LastFallbackReason = $"FFmpeg 后端不可用：{ex.Message}";
                    _logger.Warn(LastFallbackReason);
                    _activeService = null;
                }
            }
            else
            {
                LastFallbackReason = "未找到 FFmpeg。";
                _logger.Warn(LastFallbackReason);
            }
        }

        if (!settings.AllowLocalAviFallback)
        {
            var reason = string.IsNullOrWhiteSpace(LastFallbackReason)
                ? "当前未启用 FFmpeg MP4 后端。"
                : LastFallbackReason;
            throw new InvalidOperationException(
                $"{reason} 为了避免高负载录制，SnipEasy 已阻止使用内置 AVI 兜底。请安装 FFmpeg，或在设置中手动开启兼容录制。");
        }

        _activeService = _aviService;
        return await _aviService.StartAsync(settings).ConfigureAwait(false);
    }

    public async Task<CaptureRecord> StopAsync()
    {
        if (_activeService is null)
        {
            throw new InvalidOperationException("当前没有正在进行的录屏。");
        }

        var record = await _activeService.StopAsync().ConfigureAwait(false);
        _activeService = null;
        return record;
    }

    public void Dispose()
    {
        _ffmpegService.Dispose();
        _aviService.Dispose();
    }

    private string GetPreferredEngineName()
    {
        return "FFmpeg MP4 后端（可配音频路由）";
    }
}
