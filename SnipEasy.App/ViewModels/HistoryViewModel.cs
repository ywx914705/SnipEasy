using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnipEasy.App.Models;
using SnipEasy.App.Services;

namespace SnipEasy.App.ViewModels;

/// <summary>
/// ViewModel for capture history management.
/// Handles history loading, filtering, searching, and export.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly AppLogger _logger;
    private readonly CaptureHistoryService _historyService;
    private readonly AppSettings _settings;
    private readonly List<CaptureRecord> _allHistory = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private CaptureKind? _selectedKindFilter;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private CaptureRecord? _selectedRecord;

    public HistoryViewModel(
        AppLogger logger,
        CaptureHistoryService historyService,
        AppSettings settings)
    {
        _logger = logger;
        _historyService = historyService;
        _settings = settings;
    }

    /// <summary>
    /// Gets the visible history records for data binding.
    /// </summary>
    public ObservableCollection<CaptureRecord> VisibleRecords { get; } = new();

    /// <summary>
    /// Gets the total count of all history records.
    /// </summary>
    public int TotalCount => _allHistory.Count;

    /// <summary>
    /// Gets the filtered count of visible records.
    /// </summary>
    public int FilteredCount => VisibleRecords.Count;

    [RelayCommand]
    private void LoadHistory()
    {
        _allHistory.Clear();
        _allHistory.AddRange(_historyService.Load(_settings.HistoryRetentionDays));
        ApplyFilter();
    }

    [RelayCommand]
    private void AddRecord(CaptureRecord record)
    {
        _historyService.Add(_allHistory, record);
        ApplyFilter();
    }

    [RelayCommand]
    private void RemoveSelectedRecord()
    {
        if (SelectedRecord is null)
        {
            return;
        }

        _allHistory.RemoveAll(item => item.Id == SelectedRecord.Id);
        _historyService.Save(_allHistory);
        ApplyFilter();
    }

    [RelayCommand]
    private void CleanMissingRecords()
    {
        var before = _allHistory.Count;
        _allHistory.RemoveAll(record => !string.IsNullOrWhiteSpace(record.FilePath) && !System.IO.File.Exists(record.FilePath));
        var removed = before - _allHistory.Count;
        if (removed == 0)
        {
            return;
        }

        _historyService.Save(_allHistory);
        ApplyFilter();
    }

    [RelayCommand]
    private void ExportCsv()
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "导出历史记录 CSV",
            Filter = "CSV 文件|*.csv|所有文件|*.*",
            FileName = $"SnipEasy-history-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        try
        {
            _historyService.ExportCsv(_allHistory, dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Error("Export history failed.", ex);
        }
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        var filtered = _historyService.Filter(_allHistory, SelectedKindFilter, SearchQuery);
        CaptureHistoryService.ReplaceVisibleRecords(VisibleRecords, filtered);
        UpdateSummary();
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedKindFilterChanged(CaptureKind? value)
    {
        ApplyFilter();
    }

    private void UpdateSummary()
    {
        var suffix = VisibleRecords.Count == _allHistory.Count ? "" : $"（筛选后 {VisibleRecords.Count} 条）";
        SummaryText = $"共 {_allHistory.Count} 条记录{suffix}。" +
            $"图片目录：{ScreenCaptureService.ResolveScreenshotDirectory(_settings)} | " +
            $"视频目录：{ScreenCaptureService.ResolveRecordingDirectory(_settings)}";
    }
}
