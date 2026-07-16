using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using WpfPoint = System.Windows.Point;

namespace SnipEasy.App.Services;

/// <summary>
/// Manages all sticker windows, including creation, persistence, and restoration.
/// </summary>
public sealed class StickerManager : IDisposable
{
    private readonly AppPaths _paths;
    private readonly AppLogger _logger;
    private readonly List<StickerWindow> _stickers = [];
    private readonly string _stickerDirectory;
    private readonly string _stateFilePath;

    public StickerManager(AppPaths paths, AppLogger logger)
    {
        _paths = paths;
        _logger = logger;
        _stickerDirectory = Path.Combine(paths.DataDirectory, "Stickers");
        _stateFilePath = Path.Combine(paths.DataDirectory, "stickers.json");
        Directory.CreateDirectory(_stickerDirectory);
    }

    /// <summary>
    /// Gets the count of active stickers.
    /// </summary>
    public int Count => _stickers.Count;

    /// <summary>
    /// Creates a new sticker from the specified image.
    /// </summary>
    public StickerWindow CreateSticker(BitmapSource image, WpfPoint? position = null)
    {
        var sticker = StickerWindow.Create(image, position);
        sticker.StickerClosed += Sticker_StickerClosed;
        sticker.Show();
        _stickers.Add(sticker);
        _logger.Info($"Sticker created. Total stickers: {_stickers.Count}");
        return sticker;
    }

    /// <summary>
    /// Closes all stickers.
    /// </summary>
    public void CloseAll()
    {
        foreach (var sticker in _stickers.ToList())
        {
            sticker.StickerClosed -= Sticker_StickerClosed;
            sticker.Close();
        }

        _stickers.Clear();
        _logger.Info("All stickers closed.");
    }

    /// <summary>
    /// Saves the current sticker state to disk.
    /// </summary>
    public void SaveState()
    {
        try
        {
            var state = new StickerState
            {
                Stickers = _stickers.Select((s, i) => new StickerInfo
                {
                    Index = i,
                    Left = s.Left,
                    Top = s.Top,
                    Width = s.Width,
                    Height = s.Height,
                    ImagePath = SaveStickerImage(s, i)
                }).ToList()
            };

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
            _logger.Info($"Sticker state saved. Count: {state.Stickers.Count}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save sticker state: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores stickers from the saved state.
    /// </summary>
    public void RestoreState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return;
            }

            var json = File.ReadAllText(_stateFilePath);
            var state = JsonSerializer.Deserialize<StickerState>(json);
            if (state?.Stickers is null || state.Stickers.Count == 0)
            {
                return;
            }

            foreach (var info in state.Stickers)
            {
                if (string.IsNullOrWhiteSpace(info.ImagePath) || !File.Exists(info.ImagePath))
                {
                    continue;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(info.ImagePath);
                image.EndInit();
                image.Freeze();
                var sticker = CreateSticker(image, new WpfPoint(info.Left, info.Top));
                sticker.Width = info.Width;
                sticker.Height = info.Height;
            }

            _logger.Info($"Sticker state restored. Count: {_stickers.Count}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to restore sticker state: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up old sticker images.
    /// </summary>
    public void CleanupOldImages()
    {
        try
        {
            var files = Directory.GetFiles(_stickerDirectory, "sticker_*.png");
            var cutoff = DateTime.Now.AddDays(-7);

            foreach (var file in files)
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to cleanup old sticker images: {ex.Message}");
        }
    }

    public void Dispose()
    {
        CloseAll();
    }

    private void Sticker_StickerClosed(object? sender, EventArgs e)
    {
        if (sender is StickerWindow sticker)
        {
            sticker.StickerClosed -= Sticker_StickerClosed;
            _stickers.Remove(sticker);
            _logger.Info($"Sticker closed. Remaining: {_stickers.Count}");
        }
    }

    private string SaveStickerImage(StickerWindow sticker, int index)
    {
        try
        {
            if (sticker.StickerImageSource is null)
            {
                return string.Empty;
            }

            var filePath = Path.Combine(_stickerDirectory, $"sticker_{index}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(sticker.StickerImageSource));
            using var stream = File.Create(filePath);
            encoder.Save(stream);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to save sticker image: {ex.Message}");
            return string.Empty;
        }
    }

    private class StickerState
    {
        public List<StickerInfo> Stickers { get; set; } = [];
    }

    private class StickerInfo
    {
        public int Index { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string ImagePath { get; set; } = string.Empty;
    }
}
