using System.Diagnostics;
using System.IO;
using System.Text;
using Forms = System.Windows.Forms;
using SnipEasy.App.Models;
using SnipEasy.App.Native;

namespace SnipEasy.App.Services;

public sealed class FfmpegRecordingService : IRecordingService
{
    private readonly AppLogger _logger;
    private Process? _process;
    private Task? _stderrTask;
    private string _activeFilePath = "";
    private DateTimeOffset _startedAt;
    private string _stderrTail = "";
    private readonly object _pauseSync = new();
    private HashSet<uint> _suspendedThreadIds = [];
    private bool _isPaused;

    public FfmpegRecordingService(AppLogger logger)
    {
        _logger = logger;
    }

    public bool IsRecording => _process is { HasExited: false };
    public bool IsPaused
    {
        get
        {
            lock (_pauseSync)
            {
                return _isPaused && IsRecording;
            }
        }
    }

    public string EngineName => "FFmpeg MP4 后端（支持音频路由）";
    public string LastFallbackReason => "";
    public string LastError => _stderrTail;

    public static string GetBundledFfmpegDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
    }

    public static string GetBundledFfmpegPath()
    {
        return Path.Combine(GetBundledFfmpegDirectory(), "ffmpeg.exe");
    }

    internal static string BuildMissingFfmpegMessage()
    {
        return $"未找到 FFmpeg，因此 MP4 录屏、录制电脑声音和麦克风不可用。请把 ffmpeg.exe 放到：{GetBundledFfmpegPath()}，或在设置中选择已有的 ffmpeg.exe。";
    }

    public static string ResolveFfmpegPath(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.FfmpegPath) && File.Exists(settings.FfmpegPath))
        {
            return settings.FfmpegPath;
        }

        var baseDirectory = AppContext.BaseDirectory;
        var localCandidates = new[]
        {
            Path.Combine(baseDirectory, "ffmpeg.exe"),
            Path.Combine(baseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDirectory, "Tools", "ffmpeg", "ffmpeg.exe")
        };

        foreach (var candidate in localCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "";
    }

    public static bool IsAvailable(AppSettings settings)
    {
        return !string.IsNullOrWhiteSpace(ResolveFfmpegPath(settings));
    }

    public bool IsFfmpegAvailable(AppSettings settings)
    {
        return IsAvailable(settings);
    }

    string IRecordingService.ResolveFfmpegPath(AppSettings settings)
    {
        return ResolveFfmpegPath(settings);
    }

    public async Task<FfmpegDiagnosticResult> DiagnoseFfmpegAsync(AppSettings settings)
    {
        return await DiagnoseAsync(settings).ConfigureAwait(false);
    }

    public async Task<string> StartAsync(AppSettings settings)
    {
        if (IsRecording)
        {
            throw new InvalidOperationException("录屏已经在进行中。");
        }

        var ffmpegPath = ResolveFfmpegPath(settings);
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            throw new InvalidOperationException(BuildMissingFfmpegMessage());
        }

        var saveDirectory = ScreenCaptureService.ResolveRecordingDirectory(settings);
        _activeFilePath = Path.Combine(saveDirectory, $"SnipEasy_recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
        _startedAt = DateTimeOffset.Now;
        _stderrTail = "";
        lock (_pauseSync)
        {
            _isPaused = false;
            _suspendedThreadIds.Clear();
        }

        var processStart = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(settings, _activeFilePath))
        {
            processStart.ArgumentList.Add(argument);
        }

        _logger.Info($"Starting FFmpeg recorder: {ffmpegPath} {string.Join(' ', processStart.ArgumentList.Select(EscapeForLog))}");
        _process = Process.Start(processStart) ?? throw new InvalidOperationException("FFmpeg 进程启动失败。");
        _stderrTask = Task.Run(() => CaptureErrorOutputAsync(_process));
        await Task.Delay(600).ConfigureAwait(false);

        if (_process.HasExited)
        {
            var tail = await GetStderrTailAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"FFmpeg 启动后立即退出。{BuildFriendlyError(tail)}{tail}");
        }

        return _activeFilePath;
    }

    public Task PauseAsync()
    {
        return Task.Run(() =>
        {
            lock (_pauseSync)
            {
                if (_process is null || _process.HasExited)
                {
                    throw new InvalidOperationException("当前没有正在进行的录屏。");
                }

                if (_isPaused)
                {
                    return;
                }

                var suspendedThreadIds = SuspendProcessThreads(_process);
                if (suspendedThreadIds.Count == 0)
                {
                    throw new InvalidOperationException("无法暂停 FFmpeg 录屏进程。");
                }

                _suspendedThreadIds = suspendedThreadIds;
                _isPaused = true;
                _logger.Info($"FFmpeg recording paused ({suspendedThreadIds.Count} threads suspended).");
            }
        });
    }

    public Task ResumeAsync()
    {
        return Task.Run(() =>
        {
            lock (_pauseSync)
            {
                if (_process is null || _process.HasExited)
                {
                    throw new InvalidOperationException("当前没有正在进行的录屏。");
                }

                ResumeProcessThreadsIfPaused();
            }
        });
    }

    public async Task<CaptureRecord> StopAsync()
    {
        if (_process is null)
        {
            throw new InvalidOperationException("当前没有正在进行的录屏。");
        }

        try
        {
            lock (_pauseSync)
            {
                ResumeProcessThreadsIfPaused();
            }

            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteLineAsync("q").ConfigureAwait(false);
                await _process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Unable to send graceful stop to FFmpeg: {ex.Message}");
        }

        if (!_process.WaitForExit(10_000))
        {
            _logger.Warn("FFmpeg did not stop in time; killing process.");
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5_000);
        }

        if (_stderrTask is not null)
        {
            await _stderrTask.ConfigureAwait(false);
        }

        var exitCode = _process.ExitCode;
        _process.Dispose();
        _process = null;
        _stderrTask = null;
        lock (_pauseSync)
        {
            _isPaused = false;
            _suspendedThreadIds.Clear();
        }

        if (exitCode != 0 && !File.Exists(_activeFilePath))
        {
            throw new InvalidOperationException($"FFmpeg 录屏失败，退出码 {exitCode}。{BuildFriendlyError(_stderrTail)}{_stderrTail}");
        }

        if (!File.Exists(_activeFilePath) || new FileInfo(_activeFilePath).Length == 0)
        {
            throw new InvalidOperationException($"录屏文件没有生成。{BuildFriendlyError(_stderrTail)}{_stderrTail}");
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
            Watermarked = false,
            Notes = "FFmpeg MP4"
        };
    }

    public async Task<string> ListDirectShowDevicesAsync(AppSettings settings)
    {
        var result = await DiagnoseAsync(settings).ConfigureAwait(false);
        if (!result.IsAvailable)
        {
            return result.Message;
        }

        return result.AudioDevices.Count == 0
            ? "未返回音频设备。请确认 FFmpeg 支持 DirectShow，且系统存在可录制音频设备。"
            : string.Join(Environment.NewLine, result.AudioDevices);
    }

    public async Task<FfmpegDiagnosticResult> DiagnoseAsync(AppSettings settings)
    {
        var ffmpegPath = ResolveFfmpegPath(settings);
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return new FfmpegDiagnosticResult(false, "", "", [], BuildMissingFfmpegMessage());
        }

        var version = await ReadVersionAsync(ffmpegPath).ConfigureAwait(false);
        var devicesOutput = await ReadDirectShowDevicesOutputAsync(ffmpegPath).ConfigureAwait(false);
        var devices = ParseAudioDevices(devicesOutput);
        var warnings = await BuildCapabilityWarningsAsync(ffmpegPath, devicesOutput, devices, settings).ConfigureAwait(false);
        var message = devices.Count == 0
            ? "FFmpeg 可用，但未解析到音频设备。电脑无音频设备、设备被禁用或 FFmpeg 缺少 DirectShow 支持时会出现这种情况。"
            : $"FFmpeg 可用，已发现 {devices.Count} 个音频设备。";

        if (warnings.Count > 0)
        {
            message = $"{message} 发现 {warnings.Count} 条注意事项。";
        }

        return new FfmpegDiagnosticResult(true, ffmpegPath, version, devices, message, warnings);
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                lock (_pauseSync)
                {
                    ResumeProcessThreadsIfPaused();
                }

                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup during shutdown.
            }
        }

        _process?.Dispose();
    }

    private static HashSet<uint> SuspendProcessThreads(Process process)
    {
        var suspendedThreadIds = new HashSet<uint>();
        foreach (ProcessThread thread in process.Threads)
        {
            var threadId = unchecked((uint)thread.Id);
            var handle = NativeMethods.OpenThread(NativeMethods.ThreadSuspendResume, false, threadId);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                if (NativeMethods.SuspendThread(handle) != uint.MaxValue)
                {
                    suspendedThreadIds.Add(threadId);
                }
            }
            finally
            {
                _ = NativeMethods.CloseHandle(handle);
            }
        }

        return suspendedThreadIds;
    }

    private void ResumeProcessThreadsIfPaused()
    {
        if (!_isPaused)
        {
            return;
        }

        foreach (var threadId in _suspendedThreadIds)
        {
            var handle = NativeMethods.OpenThread(NativeMethods.ThreadSuspendResume, false, threadId);
            if (handle == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                _ = NativeMethods.ResumeThread(handle);
            }
            finally
            {
                _ = NativeMethods.CloseHandle(handle);
            }
        }

        _suspendedThreadIds.Clear();
        _isPaused = false;
        _logger.Info("FFmpeg recording resumed.");
    }

    internal static List<string> BuildArguments(AppSettings settings, string outputPath)
    {
        var screen = Forms.SystemInformation.VirtualScreen;
        var performanceProfile = RecordingPerformanceProfiles.Resolve(settings.RecordingPerformanceMode);
        var frameRate = Math.Clamp(settings.RecordingFrameRate <= 0 ? performanceProfile.FrameRate : settings.RecordingFrameRate, 1, 30);
        var crf = Math.Clamp(settings.RecordingCrf, 18, 35);
        var arguments = new List<string>
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "info",
            "-thread_queue_size",
            "1024",
            "-f",
            "gdigrab",
            "-draw_mouse",
            "1",
            "-framerate",
            frameRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-offset_x",
            screen.Left.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-offset_y",
            screen.Top.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-video_size",
            $"{screen.Width}x{screen.Height}",
            "-i",
            "desktop"
        };

        var audioInputCount = 0;
        AddAudioInput(settings.RecordingCaptureSystemAudio, settings.RecordingSystemAudioDevice, arguments, ref audioInputCount);
        AddAudioInput(settings.RecordingCaptureMicrophone, settings.RecordingMicrophoneDevice, arguments, ref audioInputCount);

        var videoTimestampFilter = $"setpts=N/({frameRate}*TB)";
        if (audioInputCount == 2)
        {
            arguments.Add("-filter_complex");
            arguments.Add(
                $"[0:v]{videoTimestampFilter}[vout];" +
                "[1:a][2:a]amix=inputs=2:duration=longest:normalize=0[amixed];" +
                "[amixed]asetpts=N/SR/TB[aout]");
            arguments.Add("-map");
            arguments.Add("[vout]");
            arguments.Add("-map");
            arguments.Add("[aout]");
        }
        else if (audioInputCount == 1)
        {
            arguments.Add("-filter_complex");
            arguments.Add($"[0:v]{videoTimestampFilter}[vout];[1:a]asetpts=N/SR/TB[aout]");
            arguments.Add("-map");
            arguments.Add("[vout]");
            arguments.Add("-map");
            arguments.Add("[aout]");
        }
        else
        {
            arguments.Add("-vf");
            arguments.Add(videoTimestampFilter);
            arguments.Add("-an");
        }

        arguments.Add("-c:v");
        arguments.Add("libx264");
        arguments.Add("-preset");
        arguments.Add(performanceProfile.FfmpegPreset);
        arguments.Add("-crf");
        arguments.Add(crf.ToString(System.Globalization.CultureInfo.InvariantCulture));
        arguments.Add("-pix_fmt");
        arguments.Add("yuv420p");

        if (audioInputCount > 0)
        {
            arguments.Add("-c:a");
            arguments.Add("aac");
            arguments.Add("-b:a");
            arguments.Add("192k");
            arguments.Add("-ar");
            arguments.Add("48000");
        }

        arguments.Add("-movflags");
        arguments.Add("+faststart");
        arguments.Add(outputPath);
        return arguments;
    }

    private static void AddAudioInput(bool enabled, string deviceName, List<string> arguments, ref int audioInputCount)
    {
        if (!enabled || string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        arguments.Add("-thread_queue_size");
        arguments.Add("1024");
        arguments.Add("-f");
        arguments.Add("dshow");
        arguments.Add("-i");
        arguments.Add($"audio={deviceName.Trim()}");
        audioInputCount++;
    }

    private static async Task<string> ReadVersionAsync(string ffmpegPath)
    {
        var output = await RunFfmpegForTextAsync(ffmpegPath, ["-version"], TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        var firstLine = output.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine) ? "未知版本" : firstLine.Trim();
    }

    private static Task<string> ReadDirectShowDevicesOutputAsync(string ffmpegPath)
    {
        return RunFfmpegForTextAsync(ffmpegPath, ["-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy"], TimeSpan.FromSeconds(10));
    }

    private static async Task<IReadOnlyList<string>> BuildCapabilityWarningsAsync(string ffmpegPath, string devicesOutput, IReadOnlyList<string> audioDevices, AppSettings settings)
    {
        var warnings = new List<string>();
        if (!devicesOutput.Contains("DirectShow", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未确认 DirectShow 设备枚举能力，音频设备选择可能不可用。");
        }

        var devicesText = await RunFfmpegForTextAsync(ffmpegPath, ["-hide_banner", "-devices"], TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        if (!devicesText.Contains("gdigrab", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未确认 gdigrab 屏幕采集能力，MP4 录屏可能无法启动。");
        }

        if (!devicesText.Contains("dshow", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未确认 dshow 音频采集能力，录制电脑声音或麦克风可能不可用。");
        }

        var encodersText = await RunFfmpegForTextAsync(ffmpegPath, ["-hide_banner", "-encoders"], TimeSpan.FromSeconds(6)).ConfigureAwait(false);
        if (!encodersText.Contains("libx264", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未确认 libx264 编码器，MP4 视频编码可能不可用。");
        }

        if (!encodersText.Contains("aac", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("未确认 AAC 音频编码器，带声音录屏可能不可用。");
        }

        AddSelectedAudioDeviceWarning(settings.RecordingCaptureSystemAudio, settings.RecordingSystemAudioDevice, "电脑声音", audioDevices, warnings);
        AddSelectedAudioDeviceWarning(settings.RecordingCaptureMicrophone, settings.RecordingMicrophoneDevice, "麦克风", audioDevices, warnings);
        return warnings;
    }

    private static void AddSelectedAudioDeviceWarning(bool enabled, string deviceName, string label, IReadOnlyList<string> audioDevices, List<string> warnings)
    {
        if (!enabled || string.IsNullOrWhiteSpace(deviceName) || audioDevices.Count == 0)
        {
            return;
        }

        if (!audioDevices.Contains(deviceName.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            warnings.Add($"当前{label}设备“{deviceName.Trim()}”不在 FFmpeg 返回的音频设备列表中。");
        }
    }

    internal static IReadOnlyList<string> ParseAudioDevices(string ffmpegOutput)
    {
        var devices = new List<string>();
        var inAudioSection = false;

        foreach (var rawLine in ffmpegOutput.Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = true;
                continue;
            }

            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inAudioSection = false;
                continue;
            }

            if (!inAudioSection)
            {
                continue;
            }

            var firstQuote = line.IndexOf('"');
            var lastQuote = line.LastIndexOf('"');
            if (firstQuote >= 0 && lastQuote > firstQuote)
            {
                var name = line[(firstQuote + 1)..lastQuote].Trim();
                if (!string.IsNullOrWhiteSpace(name) && !devices.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    devices.Add(name);
                }
            }
        }

        return devices;
    }

    internal static string BuildFriendlyError(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "";
        }

        var text = stderr.ToLowerInvariant();
        if (text.Contains("could not find audio only device") || text.Contains("could not find audio device"))
        {
            return "音频设备名称无效或设备当前不可用，请重新查看 FFmpeg 音频设备列表并选择正确名称。";
        }

        if (text.Contains("permission denied") || text.Contains("access is denied"))
        {
            return "输出目录没有写入权限，请更换视频保存目录或检查文件是否被占用。";
        }

        if (text.Contains("unknown encoder") || text.Contains("encoder not found"))
        {
            return "当前 FFmpeg 构建缺少所需编码器，请更换完整版本 FFmpeg。";
        }

        if (text.Contains("gdigrab") && (text.Contains("not found") || text.Contains("unknown input format")))
        {
            return "当前 FFmpeg 不支持 Windows 屏幕采集 gdigrab，请更换 Windows 完整版 FFmpeg。";
        }

        if (text.Contains("no such file or directory"))
        {
            return "FFmpeg 无法访问输入设备或输出路径，请检查路径、设备名和权限。";
        }

        return "";
    }

    private static async Task<string> RunFfmpegForTextAsync(string ffmpegPath, IEnumerable<string> arguments, TimeSpan timeout)
    {
        var processStart = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            processStart.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processStart);
        if (process is null)
        {
            return "FFmpeg 进程启动失败。";
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            return "FFmpeg 命令执行超时。";
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(text => !string.IsNullOrWhiteSpace(text))).Trim();
    }

    private async Task CaptureErrorOutputAsync(Process process)
    {
        var tail = new Queue<string>();
        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                continue;
            }

            tail.Enqueue(line);
            while (tail.Count > 24)
            {
                tail.Dequeue();
            }
        }

        _stderrTail = string.Join(Environment.NewLine, tail);
        if (!string.IsNullOrWhiteSpace(_stderrTail))
        {
            _logger.Info($"FFmpeg stderr tail: {_stderrTail}");
        }
    }

    private async Task<string> GetStderrTailAsync()
    {
        if (_stderrTask is not null)
        {
            await _stderrTask.ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(_stderrTail) ? "" : Environment.NewLine + _stderrTail;
    }

    private static string EscapeForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
