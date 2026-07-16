using System.IO;

namespace SnipEasy.App.Services;

public sealed class AppPaths
{
    private const string ProductName = "SnipEasy";
    private static readonly string LegacyDDriveRoot = @"D:\SnipEasy";

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

    public static AppPaths Create()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = AppContext.BaseDirectory;
        }

        var dataDirectory = Path.Combine(localAppData, ProductName);
        var productRoot = dataDirectory;
        var productScreenshotDirectory = GetKnownFolderDirectory(
            Environment.SpecialFolder.MyPictures,
            "Pictures");
        var productVideoDirectory = GetKnownFolderDirectory(
            Environment.SpecialFolder.MyVideos,
            "Videos");

        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(productRoot);
        Directory.CreateDirectory(productScreenshotDirectory);
        Directory.CreateDirectory(productVideoDirectory);
        try
        {
            MigrateLegacyData(dataDirectory);
        }
        catch (IOException)
        {
            // Migration is best effort. Existing data remains untouched.
        }
        catch (UnauthorizedAccessException)
        {
            // A locked-down legacy directory must not prevent application startup.
        }

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
        var directory = GetKnownFolderDirectory(Environment.SpecialFolder.MyPictures, "Pictures");
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetDefaultVideoDirectory()
    {
        var directory = GetKnownFolderDirectory(Environment.SpecialFolder.MyVideos, "Videos");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetKnownFolderDirectory(Environment.SpecialFolder folder, string fallbackFolder)
    {
        var root = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                fallbackFolder);
        }

        return Path.Combine(root, ProductName);
    }

    private static void MigrateLegacyData(string dataDirectory)
    {
        var legacyDataDirectory = Path.Combine(LegacyDDriveRoot, "Data");
        if (!Directory.Exists(legacyDataDirectory) ||
            string.Equals(legacyDataDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CopyFileIfDestinationMissing(
            Path.Combine(legacyDataDirectory, "settings.json"),
            Path.Combine(dataDirectory, "settings.json"));
        CopyFileIfDestinationMissing(
            Path.Combine(legacyDataDirectory, "history.json"),
            Path.Combine(dataDirectory, "history.json"));
        CopyFileIfDestinationMissing(
            Path.Combine(legacyDataDirectory, "snipeasy.log"),
            Path.Combine(dataDirectory, "snipeasy.log"));
        CopyDirectoryIfDestinationMissing(
            Path.Combine(legacyDataDirectory, "Stickers"),
            Path.Combine(dataDirectory, "Stickers"));
        CopyFileIfDestinationMissing(
            Path.Combine(legacyDataDirectory, "stickers.json"),
            Path.Combine(dataDirectory, "stickers.json"));
    }

    private static void CopyFileIfDestinationMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? ".");
        File.Copy(source, destination, overwrite: false);
    }

    private static void CopyDirectoryIfDestinationMissing(string source, string destination)
    {
        if (!Directory.Exists(source) || Directory.Exists(destination))
        {
            return;
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
    }
}
