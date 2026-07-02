using SnipEasy.App.Models;

namespace SnipEasy.App.Services;

public interface IRecordingService : IDisposable
{
    bool IsRecording { get; }
    string EngineName { get; }
    string LastFallbackReason { get; }
    Task<string> StartAsync(AppSettings settings);
    Task<CaptureRecord> StopAsync();
    bool IsFfmpegAvailable(AppSettings settings);
    string ResolveFfmpegPath(AppSettings settings);
    Task<FfmpegDiagnosticResult> DiagnoseFfmpegAsync(AppSettings settings);
}
