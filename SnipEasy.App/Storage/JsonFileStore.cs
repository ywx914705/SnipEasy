using System.IO;
using System.Text.Json;
using SnipEasy.App.Services;

namespace SnipEasy.App.Storage;

public sealed class JsonFileStore<T> where T : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly AppLogger? _logger;

    public JsonFileStore(string path, AppLogger? logger = null)
    {
        _path = path;
        _logger = logger;
    }

    public T LoadOrDefault(Func<T> createDefault)
    {
        if (!File.Exists(_path))
        {
            return createDefault();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<T>(json, SerializerOptions) ?? createDefault();
        }
        catch (Exception ex)
        {
            var backupPath = BackupBrokenFile();
            _logger?.Warn(string.IsNullOrWhiteSpace(backupPath)
                ? $"Unable to load JSON store {_path}; defaults will be used. {ex.Message}"
                : $"Unable to load JSON store {_path}; backed up to {backupPath}. {ex.Message}");
            return createDefault();
        }
    }

    public void Save(T value)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_path}.tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, SerializerOptions));
        File.Copy(temporaryPath, _path, overwrite: true);
        File.Delete(temporaryPath);
    }

    private string BackupBrokenFile()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return "";
            }

            var backupPath = $"{_path}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(_path, backupPath, overwrite: false);
            return backupPath;
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Unable to back up broken JSON store {_path}: {ex.Message}");
            return "";
        }
    }
}
