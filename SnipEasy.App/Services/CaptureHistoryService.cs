using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using SnipEasy.App.Models;
using SnipEasy.App.Storage;

namespace SnipEasy.App.Services;

public sealed class CaptureHistoryService
{
    public const int MaxRecords = 1000;

    private readonly JsonFileStore<List<CaptureRecord>> _store;
    private readonly AppLogger _logger;

    public CaptureHistoryService(string historyPath, AppLogger logger)
    {
        _logger = logger;
        _store = new JsonFileStore<List<CaptureRecord>>(historyPath, _logger);
    }

    public List<CaptureRecord> Load(int retentionDays)
    {
        var records = _store.LoadOrDefault(() => []);
        return Prune(records, retentionDays);
    }

    public void Save(IEnumerable<CaptureRecord> records)
    {
        _store.Save(records
            .OrderByDescending(record => record.CreatedAt)
            .Take(MaxRecords)
            .ToList());
    }

    public void Add(IList<CaptureRecord> records, CaptureRecord record)
    {
        records.Insert(0, record);
        while (records.Count > MaxRecords)
        {
            records.RemoveAt(records.Count - 1);
        }

        Save(records);
    }

    public List<CaptureRecord> Prune(IEnumerable<CaptureRecord> records, int retentionDays)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Max(retentionDays, 1));
        return records
            .Where(record => record.CreatedAt >= cutoff)
            .OrderByDescending(record => record.CreatedAt)
            .Take(MaxRecords)
            .ToList();
    }

    public IEnumerable<CaptureRecord> Filter(IEnumerable<CaptureRecord> records, CaptureKind? kind, string query)
    {
        var normalizedQuery = query.Trim();
        var filtered = records;

        if (kind is not null)
        {
            filtered = filtered.Where(record => record.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filtered = filtered.Where(record =>
                Contains(record.FileNameDisplay, normalizedQuery) ||
                Contains(record.FilePath, normalizedQuery) ||
                Contains(record.SourceWindowTitle, normalizedQuery) ||
                Contains(record.SourceProcessName, normalizedQuery) ||
                Contains(record.Notes, normalizedQuery));
        }

        return filtered.OrderByDescending(record => record.CreatedAt);
    }

    public void ExportCsv(IEnumerable<CaptureRecord> records, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("Id,Kind,CreatedAt,FilePath,FileSizeBytes,ClipboardMode,SourceWindowTitle,SourceProcessName,Watermarked,Notes");

        foreach (var record in records.OrderByDescending(record => record.CreatedAt))
        {
            builder.AppendLine(string.Join(',',
                Escape(record.Id.ToString()),
                Escape(record.Kind.ToString()),
                Escape(record.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
                Escape(record.FilePath),
                record.FileSizeBytes.ToString(CultureInfo.InvariantCulture),
                Escape(record.ClipboardMode),
                Escape(record.SourceWindowTitle),
                Escape(record.SourceProcessName),
                record.Watermarked ? "true" : "false",
                Escape(record.Notes)));
        }

        File.WriteAllText(filePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        _logger.Info($"History exported: {filePath}");
    }

    public static void ReplaceVisibleRecords(ObservableCollection<CaptureRecord> target, IEnumerable<CaptureRecord> records)
    {
        target.Clear();
        foreach (var record in records)
        {
            target.Add(record);
        }
    }

    private static bool Contains(string? value, string query)
    {
        return !string.IsNullOrEmpty(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string Escape(string? value)
    {
        var escaped = (value ?? "").Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}
