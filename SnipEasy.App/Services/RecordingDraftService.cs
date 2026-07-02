using System.IO;
using SnipEasy.App.Models;

namespace SnipEasy.App.Services;

public sealed class RecordingDraftService
{
    private readonly AppPaths _paths;

    public RecordingDraftService(AppPaths paths)
    {
        _paths = paths;
    }

    public string DraftDirectory
    {
        get
        {
            var directory = Path.Combine(_paths.DataDirectory, "RecordingDrafts");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public AppSettings CreateDraftSettings(AppSettings settings)
    {
        return new AppSettings
        {
            ScreenshotDirectory = settings.ScreenshotDirectory,
            VideoDirectory = DraftDirectory,
            SaveDirectory = settings.SaveDirectory,
            EnableWatermark = settings.EnableWatermark,
            WatermarkTemplate = settings.WatermarkTemplate,
            RecordingFrameRate = settings.RecordingFrameRate,
            RecordingCrf = settings.RecordingCrf,
            RecordingPerformanceMode = settings.RecordingPerformanceMode,
            PreferFfmpegRecording = settings.PreferFfmpegRecording,
            AllowLocalAviFallback = settings.AllowLocalAviFallback,
            FfmpegPath = settings.FfmpegPath,
            RecordingCaptureSystemAudio = settings.RecordingCaptureSystemAudio,
            RecordingSystemAudioDevice = settings.RecordingSystemAudioDevice,
            RecordingCaptureMicrophone = settings.RecordingCaptureMicrophone,
            RecordingMicrophoneDevice = settings.RecordingMicrophoneDevice,
            HistoryRetentionDays = settings.HistoryRetentionDays,
            MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose,
            FirstRunCompleted = settings.FirstRunCompleted,
            StartWithWindows = settings.StartWithWindows
        };
    }

    public string CreateUniqueFilePath(string directory, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var path = Path.Combine(directory, fileName);
        var index = 2;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName}_{index}{extension}");
            index++;
        }

        return path;
    }

    public void MoveDraft(string sourcePath, string destinationPath)
    {
        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch (IOException)
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
            File.Delete(sourcePath);
        }
    }

    public void DeleteFileIfExists(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
