namespace SnipEasy.App.Services;

public sealed record FfmpegDiagnosticResult(
    bool IsAvailable,
    string Path,
    string Version,
    IReadOnlyList<string> AudioDevices,
    string Message,
    IReadOnlyList<string>? Warnings = null);
