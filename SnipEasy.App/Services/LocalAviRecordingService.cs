using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;
using SnipEasy.App.Models;
using SnipEasy.App.Native;

namespace SnipEasy.App.Services;

public sealed class LocalAviRecordingService : IRecordingService
{
    private CancellationTokenSource? _stopSignal;
    private ManualResetEventSlim? _pauseGate;
    private Task? _recordingTask;
    private string _activeFilePath = "";
    private DateTimeOffset _startedAt;
    private volatile bool _isPaused;

    public bool IsRecording => _recordingTask is { IsCompleted: false };
    public bool IsPaused => _isPaused && IsRecording;
    public string EngineName => "内置 AVI 兜底后端（无音频）";
    public string LastFallbackReason => "";

    public bool IsFfmpegAvailable(AppSettings settings)
    {
        return false;
    }

    public string ResolveFfmpegPath(AppSettings settings)
    {
        return "";
    }

    public Task<FfmpegDiagnosticResult> DiagnoseFfmpegAsync(AppSettings settings)
    {
        return Task.FromResult(new FfmpegDiagnosticResult(false, "", "", [], "AVI 后端不支持 FFmpeg 诊断。"));
    }

    public Task<string> StartAsync(AppSettings settings)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("录屏已经在进行中。");
        }

        var saveDirectory = ScreenCaptureService.ResolveRecordingDirectory(settings);
        _activeFilePath = Path.Combine(saveDirectory, $"SnipEasy_recording_{DateTime.Now:yyyyMMdd_HHmmss}.avi");
        _startedAt = DateTimeOffset.Now;
        _stopSignal = new CancellationTokenSource();
        _pauseGate = new ManualResetEventSlim(initialState: true);
        _isPaused = false;
        var performanceProfile = RecordingPerformanceProfiles.Resolve(settings.RecordingPerformanceMode);
        var frameRate = Math.Clamp(settings.RecordingFrameRate, 1, performanceProfile.LocalAviFrameRateLimit);
        var quality = Math.Clamp(100 - settings.RecordingCrf * 2, 35, 75);

        _recordingTask = Task.Run(() => RecordLoop(_activeFilePath, frameRate, quality, _pauseGate, _stopSignal.Token));
        ObserveRecordingTaskFault(_recordingTask);

        // Recording starts in a background task; return the output path immediately.
        // The interface requires Task<string> for compatibility with FfmpegRecordingService.
        return Task.FromResult(_activeFilePath);
    }

    public Task PauseAsync()
    {
        if (!IsRecording || _pauseGate is null)
        {
            throw new InvalidOperationException("当前没有正在进行的录屏。");
        }

        if (!_isPaused)
        {
            _isPaused = true;
            _pauseGate.Reset();
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (!IsRecording || _pauseGate is null)
        {
            throw new InvalidOperationException("当前没有正在进行的录屏。");
        }

        _isPaused = false;
        _pauseGate.Set();
        return Task.CompletedTask;
    }

    public async Task<CaptureRecord> StopAsync()
    {
        if (_recordingTask is null || _stopSignal is null)
        {
            throw new InvalidOperationException("当前没有正在进行的录屏。");
        }

        _stopSignal.Cancel();
        _pauseGate?.Set();
        await _recordingTask.ConfigureAwait(false);

        _recordingTask = null;
        _stopSignal.Dispose();
        _stopSignal = null;
        _pauseGate?.Dispose();
        _pauseGate = null;
        _isPaused = false;

        if (!File.Exists(_activeFilePath) || new FileInfo(_activeFilePath).Length == 0)
        {
            throw new InvalidOperationException("录屏文件没有生成。");
        }

        var fileInfo = new FileInfo(_activeFilePath);
        var sourceWindow = NativeMethods.GetForegroundWindowInfo();

        return new CaptureRecord
        {
            Kind = CaptureKind.Recording,
            CreatedAt = _startedAt,
            FilePath = _activeFilePath,
            FileSizeBytes = fileInfo.Length,
            ClipboardMode = "File",
            SourceWindowTitle = sourceWindow.Title,
            SourceProcessName = sourceWindow.ProcessName,
            Watermarked = false
        };
    }

    public void Dispose()
    {
        if (_stopSignal is not null)
        {
            _stopSignal.Cancel();
            _pauseGate?.Set();
            try
            {
                _recordingTask?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException ex) when (ex.InnerException is OperationCanceledException)
            {
                // Expected: recording was cancelled during shutdown.
            }
            catch (AggregateException ex)
            {
                Debug.WriteLine($"LocalAviRecordingService.Dispose cleanup error: {ex.InnerException?.Message}");
            }
            catch (OperationCanceledException)
            {
                // Expected: recording was cancelled during shutdown.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalAviRecordingService.Dispose cleanup error: {ex.Message}");
            }

            _stopSignal.Dispose();
            _pauseGate?.Dispose();
            _pauseGate = null;
            _isPaused = false;
        }
    }

    /// <summary>
    /// Observes the recording task's fault to prevent unobserved task exceptions.
    /// </summary>
    private static void ObserveRecordingTaskFault(Task task)
    {
        task.ContinueWith(
            t => Debug.WriteLine($"LocalAviRecordingService recording task faulted: {t.Exception?.InnerException?.Message}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static void RecordLoop(
        string outputPath,
        int frameRate,
        int quality,
        ManualResetEventSlim pauseGate,
        CancellationToken token)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        using var avi = new AviWriter(outputPath, screen.Width, screen.Height, frameRate);
        var delay = TimeSpan.FromMilliseconds(1000.0 / frameRate);
        var nextFrameAt = DateTime.UtcNow;

        using var bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);

        while (!token.IsCancellationRequested)
        {
            if (!pauseGate.IsSet)
            {
                pauseGate.Wait();
                nextFrameAt = DateTime.UtcNow;
                if (token.IsCancellationRequested)
                {
                    break;
                }
            }

            graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);

            avi.AddFrame(EncodeJpeg(bitmap, quality));

            nextFrameAt += delay;
            var wait = nextFrameAt - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                token.WaitHandle.WaitOne(wait);
            }
        }
    }

    private static byte[] EncodeJpeg(Bitmap bitmap, int quality)
    {
        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        bitmap.Save(stream, encoder, parameters);
        return stream.ToArray();
    }
}
