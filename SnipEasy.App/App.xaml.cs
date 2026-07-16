using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SnipEasy.App.Models;
using SnipEasy.App.Services;
using SnipEasy.App.Storage;

namespace SnipEasy.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ServiceProvider? _serviceProvider;
    private CrashReportService? _crashReportService;

    public App()
    {
        var paths = AppPaths.Create();
        var logger = new AppLogger(paths.LogPath);
        _crashReportService = new CrashReportService(paths, logger);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(arg => string.Equals(arg, "--capture-once", StringComparison.OrdinalIgnoreCase)))
        {
            CaptureOnceAndExit();
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--record-test", StringComparison.OrdinalIgnoreCase)))
        {
            RecordTestAndExit();
            return;
        }

        if (e.Args.Any(arg => string.Equals(arg, "--record-pause-test", StringComparison.OrdinalIgnoreCase)))
        {
            RecordPauseTestAndExit();
            return;
        }

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "SnipEasy.Desktop.SingleInstance", out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            System.Windows.MessageBox.Show(
                "SnipEasy 已经在运行。请在系统托盘中打开已运行的实例。",
                "SnipEasy",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        // Initialize DI container
        _serviceProvider = ServiceCollectionExtensions.BuildSnipEasyServiceProvider();

        // Create main window with dependency injection
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var packagePath = _crashReportService?.WriteCrashReport(e.Exception, "DispatcherUnhandledException") ?? "";
        System.Windows.MessageBox.Show(
            string.IsNullOrWhiteSpace(packagePath)
                ? "SnipEasy 遇到未处理异常，程序将尝试继续运行。"
                : $"SnipEasy 遇到未处理异常，已生成诊断包：{packagePath}",
            "SnipEasy",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _ = _crashReportService?.WriteCrashReport(exception, "UnhandledException");
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _ = _crashReportService?.WriteCrashReport(e.Exception, "UnobservedTaskException");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void CaptureOnceAndExit()
    {
        AppLogger? logger = null;
        try
        {
            var paths = AppPaths.Create();
            logger = new AppLogger(paths.LogPath);
            var settingsStore = new JsonFileStore<AppSettings>(paths.SettingsPath);
            var historyStore = new JsonFileStore<List<CaptureRecord>>(paths.HistoryPath);
            var settings = settingsStore.LoadOrDefault(() => new AppSettings
            {
                SaveDirectory = paths.DefaultCaptureDirectory
            });

            var captureService = new ScreenCaptureService();
            var clipboardService = new ClipboardService();
            var result = captureService.CaptureFullScreen(settings);

            if (result.Image is not null)
            {
                clipboardService.SetImage(result.Image);
            }

            var history = historyStore.LoadOrDefault(() => []);
            history.Insert(0, result.Record);
            historyStore.Save(history.Take(1000).ToList());
            ExitHeadless(0);
        }
        catch (Exception ex)
        {
            logger?.Error("CaptureOnce failed", ex);
            TryWriteCrashReport(ex, "CaptureOnceAndExit", logger);
            ExitHeadless(1);
        }
    }

    private void RecordTestAndExit()
    {
        AppLogger? logger = null;
        try
        {
            var paths = AppPaths.Create();
            logger = new AppLogger(paths.LogPath);
            var settingsStore = new JsonFileStore<AppSettings>(paths.SettingsPath);
            var historyStore = new JsonFileStore<List<CaptureRecord>>(paths.HistoryPath);
            var settings = settingsStore.LoadOrDefault(() => new AppSettings
            {
                SaveDirectory = paths.DefaultCaptureDirectory,
                RecordingPerformanceMode = RecordingPerformanceProfiles.Smooth,
                RecordingFrameRate = 8
            });

            settings.RecordingFrameRate = Math.Clamp(settings.RecordingFrameRate, 1, 8);

            using var recordingService = new LocalAviRecordingService();
            // StartAsync returns synchronously (Task.FromResult) — no deadlock risk.
            var outputPath = recordingService.StartAsync(settings).GetAwaiter().GetResult();
            Thread.Sleep(TimeSpan.FromMilliseconds(750));
            recordingService.PauseAsync().GetAwaiter().GetResult();
            Thread.Sleep(TimeSpan.FromMilliseconds(500));
            recordingService.ResumeAsync().GetAwaiter().GetResult();
            Thread.Sleep(TimeSpan.FromMilliseconds(750));
            var record = recordingService.StopAsync().GetAwaiter().GetResult();

            var clipboardService = new ClipboardService();
            clipboardService.SetFileDrop(outputPath);

            var history = historyStore.LoadOrDefault(() => []);
            history.Insert(0, record);
            historyStore.Save(history.Take(1000).ToList());
            ExitHeadless(0);
        }
        catch (Exception ex)
        {
            logger?.Error("RecordTest failed", ex);
            TryWriteCrashReport(ex, "RecordTestAndExit", logger);
            ExitHeadless(1);
        }
    }

    private void RecordPauseTestAndExit()
    {
        AppLogger? logger = null;
        try
        {
            var paths = AppPaths.Create();
            logger = new AppLogger(paths.LogPath);
            var settingsStore = new JsonFileStore<AppSettings>(paths.SettingsPath);
            var historyStore = new JsonFileStore<List<CaptureRecord>>(paths.HistoryPath);
            var settings = settingsStore.LoadOrDefault(() => new AppSettings());
            settings.PreferFfmpegRecording = true;
            settings.AllowLocalAviFallback = false;
            settings.RecordingCaptureSystemAudio = false;
            settings.RecordingCaptureMicrophone = false;
            settings.RecordingFrameRate = Math.Clamp(settings.RecordingFrameRate, 4, 12);

            using var recordingService = new RecordingServiceCoordinator(logger);
            var outputPath = recordingService.StartAsync(settings).GetAwaiter().GetResult();
            Thread.Sleep(TimeSpan.FromSeconds(1));
            recordingService.PauseAsync().GetAwaiter().GetResult();
            if (!recordingService.IsPaused)
            {
                throw new InvalidOperationException("FFmpeg pause smoke test did not enter the paused state.");
            }

            Thread.Sleep(TimeSpan.FromSeconds(2));
            recordingService.ResumeAsync().GetAwaiter().GetResult();
            if (recordingService.IsPaused)
            {
                throw new InvalidOperationException("FFmpeg pause smoke test did not resume recording.");
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
            var record = recordingService.StopAsync().GetAwaiter().GetResult();

            var clipboardService = new ClipboardService();
            clipboardService.SetFileDrop(outputPath);

            var history = historyStore.LoadOrDefault(() => []);
            history.Insert(0, record);
            historyStore.Save(history.Take(1000).ToList());
            ExitHeadless(0);
        }
        catch (Exception ex)
        {
            logger?.Error("RecordPauseTest failed", ex);
            TryWriteCrashReport(ex, "RecordPauseTestAndExit", logger);
            ExitHeadless(1);
        }
    }

    private void TryWriteCrashReport(Exception ex, string context, AppLogger? logger)
    {
        try
        {
            var paths = AppPaths.Create();
            var crashService = new CrashReportService(paths, logger ?? new AppLogger(paths.LogPath));
            crashService.WriteCrashReport(ex, context);
        }
        catch
        {
            // Last resort: cannot write crash report. The error was already logged above.
        }
    }

    private static void ExitHeadless(int exitCode)
    {
        Environment.ExitCode = exitCode;
        Environment.Exit(exitCode);
    }
}
