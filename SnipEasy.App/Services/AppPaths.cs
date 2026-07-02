using System.IO;

namespace SnipEasy.App.Services;

public sealed class AppPaths
{
    public required string DataDirectory { get; init; }
    public required string SettingsPath { get; init; }
    public required string HistoryPath { get; init; }
    public required string LogPath { get; init; }
    public required string ProductRootDirectory { get; init; }
    public required string DefaultScreenshotDirectory { get; init; }
    public required string DefaultVideoDirectory { get; init; }

    /// <summary>
    /// Alias for <see cref="DefaultScreenshotDirectory"/>. Kept for backward compatibility.
    /// </summary>
    public string DefaultCaptureDirectory => DefaultScreenshotDirectory;

    private static readonly string DDriveRoot = @"D:\SnipEasy";

    public static AppPaths Create()
    {
        // 所有数据都存储在 D 盘
        var dataDirectory = Path.Combine(DDriveRoot, "Data");
        var productRoot = DDriveRoot;
        var productScreenshotDirectory = Path.Combine(productRoot, "Screenshots");
        var productVideoDirectory = Path.Combine(productRoot, "Videos");

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(productRoot);
        Directory.CreateDirectory(productScreenshotDirectory);
        Directory.CreateDirectory(productVideoDirectory);

        return new AppPaths
        {
            DataDirectory = dataDirectory,
            SettingsPath = Path.Combine(dataDirectory, "settings.json"),
            HistoryPath = Path.Combine(dataDirectory, "history.json"),
            LogPath = Path.Combine(dataDirectory, "snipeasy.log"),
            ProductRootDirectory = productRoot,
            DefaultScreenshotDirectory = productScreenshotDirectory,
            DefaultVideoDirectory = productVideoDirectory
        };
    }

    public static string GetDefaultScreenshotDirectory()
    {
        var directory = Path.Combine(DDriveRoot, "Screenshots");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetDefaultVideoDirectory()
    {
        var directory = Path.Combine(DDriveRoot, "Videos");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
