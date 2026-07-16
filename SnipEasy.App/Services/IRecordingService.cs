using SnipEasy.App.Models;

namespace SnipEasy.App.Services;

public interface IRecordingService : IDisposable
{
    bool IsRecording { get; }
    bool IsPaused { get; }
    string EngineName { get; }
    string LastFallbackReason { get; }
    Task<string> StartAsync(AppSettings settings);
    Task PauseAsync();
    Task ResumeAsync();
    Task<CaptureRecord> StopAsync();
    bool IsFfmpegAvailable(AppSettings settings);
    string ResolveFfmpegPath(AppSettings settings);
    Task<FfmpegDiagnosticResult> DiagnoseFfmpegAsync(AppSettings settings);
}
