using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfImage = System.Windows.Controls.Image;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfPoint = System.Windows.Point;

namespace SnipEasy.App;

/// <summary>
/// A color picker window that captures colors from the screen with a magnifier preview.
/// Supports HEX, RGB, and HSL color formats.
/// </summary>
public partial class ColorPickerWindow : Window
{
    private const int MagnifierZoom = 10;
    private const int MagnifierSize = 16;
    private const int MaxHistoryColors = 10;

    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<SolidColorBrush> _historyColors = new();
    private WriteableBitmap? _magnifierBitmap;
    private WpfColor _currentColor;

    public ColorPickerWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += Timer_Tick;

        HistoryColors.ItemsSource = _historyColors;
        Loaded += ColorPickerWindow_Loaded;
        Closed += ColorPickerWindow_Closed;
    }

    /// <summary>
    /// Event raised when a color is selected (clicked).
    /// </summary>
    public event EventHandler<WpfColor>? ColorSelected;

    /// <summary>
    /// Gets the currently hovered color.
    /// </summary>
    public WpfColor CurrentColor => _currentColor;

    /// <summary>
    /// Shows the color picker and starts tracking.
    /// </summary>
    public static ColorPickerWindow ShowPicker()
    {
        var picker = new ColorPickerWindow();
        picker.Show();
        return picker;
    }

    private void ColorPickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Position near cursor
        var cursorPos = GetCursorPos();
        Left = cursorPos.X + 20;
        Top = cursorPos.Y + 20;

        // Ensure within screen bounds
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        if (Left + ActualWidth > screenWidth)
        {
            Left = cursorPos.X - ActualWidth - 20;
        }
        if (Top + ActualHeight > screenHeight)
        {
            Top = cursorPos.Y - ActualHeight - 20;
        }

        _magnifierBitmap = new WriteableBitmap(
            MagnifierSize * MagnifierZoom,
            MagnifierSize * MagnifierZoom,
            96, 96,
            PixelFormats.Pbgra32,
            null);

        _timer.Start();
        CaptureMouse();
    }

    private void ColorPickerWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateMagnifier();
    }

    private void UpdateMagnifier()
    {
        var cursorPos = GetCursorPos();

        // Capture screen region around cursor
        var screenBitmap = CaptureScreenRegion(
            (int)cursorPos.X - MagnifierSize / 2,
            (int)cursorPos.Y - MagnifierSize / 2,
            MagnifierSize,
            MagnifierSize);

        if (screenBitmap is null || _magnifierBitmap is null)
        {
            return;
        }

        // Get center pixel color
        _currentColor = GetPixelColor(screenBitmap, MagnifierSize / 2, MagnifierSize / 2);
        UpdateColorDisplays();

        // Draw magnifier
        DrawMagnifier(screenBitmap);
    }

    private void DrawMagnifier(BitmapSource source)
    {
        if (_magnifierBitmap is null)
        {
            return;
        }

        _magnifierBitmap.Lock();

        // Clear
        var clearPixels = new byte[_magnifierBitmap.PixelWidth * _magnifierBitmap.PixelHeight * 4];
        _magnifierBitmap.WritePixels(
            new Int32Rect(0, 0, _magnifierBitmap.PixelWidth, _magnifierBitmap.PixelHeight),
            clearPixels,
            _magnifierBitmap.PixelWidth * 4,
            0);

        // Read source pixels
        var sourcePixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        source.CopyPixels(sourcePixels, source.PixelWidth * 4, 0);

        // Draw zoomed pixels
        for (int y = 0; y < MagnifierSize; y++)
        {
            for (int x = 0; x < MagnifierSize; x++)
            {
                var srcOffset = (y * source.PixelWidth + x) * 4;
                var b = sourcePixels[srcOffset];
                var g = sourcePixels[srcOffset + 1];
                var r = sourcePixels[srcOffset + 2];
                var a = sourcePixels[srcOffset + 3];

                // Draw zoomed pixel block
                for (int dy = 0; dy < MagnifierZoom; dy++)
                {
                    for (int dx = 0; dx < MagnifierZoom; dx++)
                    {
                        var dstX = x * MagnifierZoom + dx;
                        var dstY = y * MagnifierZoom + dy;
                        var dstOffset = (dstY * _magnifierBitmap.PixelWidth + dstX) * 4;
                        clearPixels[dstOffset] = b;
                        clearPixels[dstOffset + 1] = g;
                        clearPixels[dstOffset + 2] = r;
                        clearPixels[dstOffset + 3] = a;
                    }
                }
            }
        }

        // Draw center crosshair
        var centerX = MagnifierSize / 2 * MagnifierZoom;
        var centerY = MagnifierSize / 2 * MagnifierZoom;
        for (int i = 0; i < MagnifierZoom; i++)
        {
            // Horizontal line
            var hOffset = (centerY * _magnifierBitmap.PixelWidth + centerX + i) * 4;
            clearPixels[hOffset] = 255;
            clearPixels[hOffset + 1] = 255;
            clearPixels[hOffset + 2] = 255;
            clearPixels[hOffset + 3] = 200;

            // Vertical line
            var vOffset = ((centerY + i) * _magnifierBitmap.PixelWidth + centerX) * 4;
            clearPixels[vOffset] = 255;
            clearPixels[vOffset + 1] = 255;
            clearPixels[vOffset + 2] = 255;
            clearPixels[vOffset + 3] = 200;
        }

        _magnifierBitmap.WritePixels(
            new Int32Rect(0, 0, _magnifierBitmap.PixelWidth, _magnifierBitmap.PixelHeight),
            clearPixels,
            _magnifierBitmap.PixelWidth * 4,
            0);

        _magnifierBitmap.Unlock();

        // Update canvas
        MagnifierCanvas.Children.Clear();
        var image = new WpfImage
        {
            Source = _magnifierBitmap,
            Width = _magnifierBitmap.PixelWidth,
            Height = _magnifierBitmap.PixelHeight
        };
        Canvas.SetLeft(image, 0);
        Canvas.SetTop(image, 0);
        MagnifierCanvas.Children.Add(image);
    }

    private void UpdateColorDisplays()
    {
        ColorPreview.Background = new SolidColorBrush(_currentColor);
        HexValue.Text = ColorToHex(_currentColor);
        RgbValue.Text = ColorToRgb(_currentColor);
        HslValue.Text = ColorToHsl(_currentColor);
    }

    private static string ColorToHex(WpfColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string ColorToRgb(WpfColor color)
    {
        return $"rgb({color.R}, {color.G}, {color.B})";
    }

    private static string ColorToHsl(WpfColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h = 0, s = 0, l = (max + min) / 2;

        if (delta > 0)
        {
            s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);

            if (max == r)
            {
                h = (g - b) / delta + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = (b - r) / delta + 2;
            }
            else
            {
                h = (r - g) / delta + 4;
            }

            h /= 6;
        }

        return $"hsl({h * 360:F0}, {s * 100:F0}%, {l * 100:F0}%)";
    }

    private static BitmapSource? CaptureScreenRegion(int x, int y, int width, int height)
    {
        try
        {
            var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
            }

            var hBitmap = bitmap.GetHbitmap();
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Native.NativeMethods.DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static WpfColor GetPixelColor(BitmapSource source, int x, int y)
    {
        var pixels = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return WpfColor.FromArgb(pixels[3], pixels[2], pixels[1], pixels[0]);
    }

    private static WpfPoint GetCursorPos()
    {
        Native.NativeMethods.GetCursorPos(out var point);
        return new WpfPoint(point.X, point.Y);
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(WpfMouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        // Select current color
        AddToHistory(_currentColor);
        ColorSelected?.Invoke(this, _currentColor);

        // Copy to clipboard
        var hex = ColorToHex(_currentColor);
        System.Windows.Clipboard.SetText(hex);
        Close();
    }

    private void CopyHex_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(HexValue.Text);
        AddToHistory(_currentColor);
        ColorSelected?.Invoke(this, _currentColor);
        Close();
    }

    private void CopyRgb_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(RgbValue.Text);
        AddToHistory(_currentColor);
        ColorSelected?.Invoke(this, _currentColor);
        Close();
    }

    private void CopyHsl_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(HslValue.Text);
        AddToHistory(_currentColor);
        ColorSelected?.Invoke(this, _currentColor);
        Close();
    }

    private void HistoryColor_Click(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is Border border && border.Background is SolidColorBrush brush)
        {
            var color = brush.Color;
            System.Windows.Clipboard.SetText(ColorToHex(color));
            ColorSelected?.Invoke(this, color);
            Close();
        }
    }

    private void AddToHistory(WpfColor color)
    {
        // Remove if already exists
        var existing = _historyColors.FirstOrDefault(b => b.Color == color);
        if (existing != null)
        {
            _historyColors.Remove(existing);
        }

        // Add to front
        _historyColors.Insert(0, new SolidColorBrush(color));

        // Trim to max
        while (_historyColors.Count > MaxHistoryColors)
        {
            _historyColors.RemoveAt(_historyColors.Count - 1);
        }
    }
}
