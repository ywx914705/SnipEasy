using System.IO;

namespace SnipEasy.App.Services;

public sealed class AppLogger
{
    private const long MaxLogBytes = 2 * 1024 * 1024;
    private const int MaxArchiveCount = 3;

    private readonly string _path;
    private readonly object _gate = new();

    public AppLogger(string path)
    {
        _path = path;
    }

    public void Info(string message)
    {
        Write("INFO", message);
    }

    public void Warn(string message)
    {
        Write("WARN", message);
    }

    public void Error(string message, Exception? exception = null)
    {
        Write("ERROR", exception is null ? message : $"{message} | {exception}");
    }

    public void LogEnvironment(AppPaths paths)
    {
        Info($"SnipEasy started. OS={Environment.OSVersion}; .NET={Environment.Version}; BaseDirectory={AppContext.BaseDirectory}; Data={paths.DataDirectory}");
    }

    private void Write(string level, string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {message}{Environment.NewLine}";
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, line);
            }
        }
        catch
        {
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path) || new FileInfo(_path).Length < MaxLogBytes)
        {
            return;
        }

        for (var index = MaxArchiveCount - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            if (File.Exists(source))
            {
                File.Move(source, destination);
            }
        }

        var firstArchive = $"{_path}.1";
        if (File.Exists(firstArchive))
        {
            File.Delete(firstArchive);
        }

        File.Move(_path, firstArchive);
    }
}
