namespace SnipEasy.App.Models;

public static class RecordingPerformanceProfiles
{
    public const string Smooth = "Smooth";
    public const string Balanced = "Balanced";
    public const string Quality = "Quality";

    public static readonly RecordingPerformanceProfile[] All =
    [
        new(Smooth, "流畅优先", 8, 30, "低帧率、低 CPU 占用，适合长时间录制和普通电脑。", "ultrafast", 60, "2500k", 6),
        new(Balanced, "均衡", 12, 26, "兼顾清晰度和流畅度，适合教程、反馈和日常演示。", "veryfast", 72, "4500k", 8),
        new(Quality, "清晰优先", 24, 22, "画面更顺滑，适合配置较好的电脑和短时高清录制。", "faster", 84, "8000k", 8)
    ];

    public static RecordingPerformanceProfile Resolve(string? id)
    {
        return All.FirstOrDefault(profile => string.Equals(profile.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    }

    public static string NormalizeMode(string? id)
    {
        return Resolve(id).Id;
    }

    public static void ApplyDefaults(AppSettings settings)
    {
        var profile = Resolve(settings.RecordingPerformanceMode);
        settings.RecordingPerformanceMode = profile.Id;
        settings.RecordingFrameRate = profile.FrameRate;
        settings.RecordingCrf = profile.Crf;
    }
}

public sealed record RecordingPerformanceProfile(
    string Id,
    string DisplayName,
    int FrameRate,
    int Crf,
    string Description,
    string FfmpegPreset,
    int FfmpegQuality,
    string FfmpegVideoBitrate,
    int LocalAviFrameRateLimit);
