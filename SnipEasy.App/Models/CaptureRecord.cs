using System.IO;

namespace SnipEasy.App.Models;

public sealed class CaptureRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CaptureKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string FilePath { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string ClipboardMode { get; set; } = "";
    public string SourceWindowTitle { get; set; } = "";
    public string SourceProcessName { get; set; } = "";
    public bool Watermarked { get; set; }
    public string Notes { get; set; } = "";

    [System.Text.Json.Serialization.JsonIgnore]
    public string KindDisplay => Kind switch
    {
        CaptureKind.Screenshot => "截图",
        CaptureKind.Recording => "录屏",
        _ => "未知"
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string ClipboardDisplay => ClipboardMode switch
    {
        "Image" => "图片",
        "File" => "文件",
        _ => ClipboardMode
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public string FileSizeDisplay
    {
        get
        {
            if (FileSizeBytes <= 0)
            {
                return "-";
            }

            var value = FileSizeBytes;
            string[] units = ["B", "KB", "MB", "GB"];
            var index = 0;
            var display = (double)value;
            while (display >= 1024 && index < units.Length - 1)
            {
                display /= 1024;
                index++;
            }

            return index == 0 ? $"{value} {units[index]}" : $"{display:0.0} {units[index]}";
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public string FileNameDisplay => string.IsNullOrWhiteSpace(FilePath) ? "" : Path.GetFileName(FilePath);
}
