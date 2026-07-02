using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using SnipEasy.App.Models;
using SnipEasy.App.Native;

namespace SnipEasy.App.Services;

public sealed class ScreenCaptureService
{
    public BitmapSource CaptureVirtualDesktop()
    {
        var virtualScreen = Forms.SystemInformation.VirtualScreen;

        using var bitmap = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(virtualScreen.Left, virtualScreen.Top, 0, 0, virtualScreen.Size, CopyPixelOperation.SourceCopy);
        }

        return ToBitmapSource(bitmap);
    }

    public BitmapSource Crop(BitmapSource source, Int32Rect pixelRegion)
    {
        if (pixelRegion.Width <= 0 || pixelRegion.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelRegion), "截图区域无效。");
        }

        var x = Math.Clamp(pixelRegion.X, 0, Math.Max(0, source.PixelWidth - 1));
        var y = Math.Clamp(pixelRegion.Y, 0, Math.Max(0, source.PixelHeight - 1));
        var safeRegion = new Int32Rect(
            x,
            y,
            Math.Min(pixelRegion.Width, source.PixelWidth - x),
            Math.Min(pixelRegion.Height, source.PixelHeight - y));

        var cropped = new CroppedBitmap(source, safeRegion);
        cropped.Freeze();
        return cropped;
    }

    internal CaptureResult SaveScreenshot(BitmapSource image, AppSettings settings, bool watermarked, ForegroundWindowInfo sourceWindow)
    {
        var saveDirectory = ResolveScreenshotDirectory(settings);
        return SaveScreenshotToDirectory(image, saveDirectory, watermarked, sourceWindow, "Image", "");
    }

    internal CaptureResult SaveScreenshotToDirectory(
        BitmapSource image,
        string saveDirectory,
        bool watermarked,
        ForegroundWindowInfo sourceWindow,
        string clipboardMode,
        string notes)
    {
        Directory.CreateDirectory(saveDirectory);
        var filePath = Path.Combine(saveDirectory, $"SnipEasy_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");

        SavePng(image, filePath);

        var fileInfo = new FileInfo(filePath);
        return new CaptureResult
        {
            Image = image,
            Record = new CaptureRecord
            {
                Kind = CaptureKind.Screenshot,
                CreatedAt = DateTimeOffset.Now,
                FilePath = filePath,
                FileSizeBytes = fileInfo.Length,
                ClipboardMode = clipboardMode,
                SourceWindowTitle = sourceWindow.Title,
                SourceProcessName = sourceWindow.ProcessName,
                Watermarked = watermarked,
                Notes = notes
            }
        };
    }

    public CaptureResult CaptureFullScreen(AppSettings settings)
    {
        var saveDirectory = ResolveScreenshotDirectory(settings);
        var filePath = Path.Combine(saveDirectory, $"SnipEasy_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var virtualScreen = Forms.SystemInformation.VirtualScreen;
        var sourceWindow = NativeMethods.GetForegroundWindowInfo();

        using var bitmap = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(virtualScreen.Left, virtualScreen.Top, 0, 0, virtualScreen.Size, CopyPixelOperation.SourceCopy);

            if (settings.EnableWatermark)
            {
                DrawWatermark(graphics, bitmap.Size, ExpandWatermark(settings.WatermarkTemplate));
            }
        }

        bitmap.Save(filePath, ImageFormat.Png);
        var image = ToBitmapSource(bitmap);

        var fileInfo = new FileInfo(filePath);
        return new CaptureResult
        {
            Image = image,
            Record = new CaptureRecord
            {
                Kind = CaptureKind.Screenshot,
                CreatedAt = DateTimeOffset.Now,
                FilePath = filePath,
                FileSizeBytes = fileInfo.Length,
                ClipboardMode = "Image",
                SourceWindowTitle = sourceWindow.Title,
                SourceProcessName = sourceWindow.ProcessName,
                Watermarked = settings.EnableWatermark
            }
        };
    }

    public static string ResolveSaveDirectory(AppSettings settings)
    {
        return ResolveScreenshotDirectory(settings);
    }

    public static string ResolveScreenshotDirectory(AppSettings settings)
    {
        var directory = string.IsNullOrWhiteSpace(settings.ScreenshotDirectory)
            ? AppPaths.GetDefaultScreenshotDirectory()
            : settings.ScreenshotDirectory.Trim();

        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string ResolveRecordingDirectory(AppSettings settings)
    {
        var directory = string.IsNullOrWhiteSpace(settings.VideoDirectory)
            ? AppPaths.GetDefaultVideoDirectory()
            : settings.VideoDirectory.Trim();

        Directory.CreateDirectory(directory);
        return directory;
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            _ = NativeMethods.DeleteObject(hBitmap);
        }
    }

    private static void SavePng(BitmapSource image, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? ".");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    private static void DrawWatermark(Graphics graphics, System.Drawing.Size canvasSize, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var font = new System.Drawing.Font("Segoe UI", 14, System.Drawing.FontStyle.Regular, GraphicsUnit.Pixel);
        var padding = 18f;
        var textSize = graphics.MeasureString(text, font);
        var box = new RectangleF(
            canvasSize.Width - textSize.Width - padding * 2,
            canvasSize.Height - textSize.Height - padding * 2,
            textSize.Width + padding,
            textSize.Height + padding);

        using var background = new SolidBrush(Color.FromArgb(160, 18, 24, 32));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, box);
        graphics.DrawString(text, font, foreground, box.Left + padding / 2, box.Top + padding / 2);
    }

    public static string ExpandWatermark(string template)
    {
        return template
            .Replace("{UserName}", Environment.UserName, StringComparison.OrdinalIgnoreCase)
            .Replace("{MachineName}", Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            .Replace("{Timestamp}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
    }
}
