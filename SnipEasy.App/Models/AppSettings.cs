namespace SnipEasy.App.Models;

public sealed class AppSettings
{
    public string ScreenshotDirectory { get; set; } = "";
    public string VideoDirectory { get; set; } = "";
    // Kept for older settings.json files. New UI uses ScreenshotDirectory and VideoDirectory.
    public string SaveDirectory { get; set; } = "";
    public bool EnableWatermark { get; set; }
    public string WatermarkTemplate { get; set; } = "";
    public string RecordingPerformanceMode { get; set; } = "";
    public int RecordingFrameRate { get; set; } = 12;
    public int RecordingCrf { get; set; } = 23;
    public bool PreferFfmpegRecording { get; set; } = true;
    public bool AllowLocalAviFallback { get; set; }
    public string FfmpegPath { get; set; } = "";
    public bool RecordingCaptureSystemAudio { get; set; }
    public string RecordingSystemAudioDevice { get; set; } = "virtual-audio-capturer";
    public bool RecordingCaptureMicrophone { get; set; }
    public string RecordingMicrophoneDevice { get; set; } = "";
    public int HistoryRetentionDays { get; set; } = 90;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool FirstRunCompleted { get; set; }
    public bool StartWithWindows { get; set; }
    public int CaptureDelaySeconds { get; set; }

    // Hotkey settings
    public HotkeySettings Hotkeys { get; set; } = new();
}

public sealed class HotkeySettings
{
    public string Screenshot { get; set; } = "F1";
    public string Recording { get; set; } = "F2";
    public string Sticker { get; set; } = "F3";
    public string ColorPicker { get; set; } = "F4";
}
