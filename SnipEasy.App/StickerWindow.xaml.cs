using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace SnipEasy.App;

/// <summary>
/// A borderless, topmost window that displays a captured screenshot as a sticker.
/// Supports dragging, resizing, and closing.
/// </summary>
public partial class StickerWindow : Window
{
    private const double ZoomFactor = 1.1;
    private const double MinScale = 0.1;
    private const double MaxScale = 10.0;

    private WpfPoint _dragStart;
    private bool _isDragging;
    private double _scale = 1.0;
    private double _baseWidth;
    private double _baseHeight;

    public StickerWindow()
    {
        InitializeComponent();
        Loaded += StickerWindow_Loaded;
        MouseLeftButtonDown += StickerWindow_MouseLeftButtonDown;
        MouseMove += StickerWindow_MouseMove;
        MouseLeftButtonUp += StickerWindow_MouseLeftButtonUp;
        MouseEnter += StickerWindow_MouseEnter;
        MouseLeave += StickerWindow_MouseLeave;
        MouseDoubleClick += StickerWindow_MouseDoubleClick;
        MouseWheel += StickerWindow_MouseWheel;
    }

    /// <summary>
    /// Gets or sets the sticker image to display.
    /// </summary>
    public BitmapSource? StickerImageSource
    {
        get => StickerImage.Source as BitmapSource;
        set => StickerImage.Source = value;
    }

    /// <summary>
    /// Event raised when the sticker is closed.
    /// </summary>
    public event EventHandler? StickerClosed;

    /// <summary>
    /// Creates a new sticker window at the specified position.
    /// </summary>
    public static StickerWindow Create(BitmapSource image, WpfPoint? position = null)
    {
        var sticker = new StickerWindow
        {
            StickerImageSource = image
        };

        if (position.HasValue)
        {
            sticker.Left = position.Value.X;
            sticker.Top = position.Value.Y;
        }
        else
        {
            // Center on screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            sticker.Left = (screenWidth - image.PixelWidth) / 2;
            sticker.Top = (screenHeight - image.PixelHeight) / 2;
        }

        return sticker;
    }

    private void StickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Record the initial image display size as the base for zoom
        _baseWidth = StickerImage.ActualWidth > 0 ? StickerImage.ActualWidth : StickerImage.MaxWidth;
        _baseHeight = StickerImage.ActualHeight > 0 ? StickerImage.ActualHeight : StickerImage.MaxHeight;

        // Ensure the window is within screen bounds
        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;

        if (Left + ActualWidth > screenWidth)
        {
            Left = screenWidth - ActualWidth - 20;
        }

        if (Top + ActualHeight > screenHeight)
        {
            Top = screenHeight - ActualHeight - 20;
        }

        if (Left < 0)
        {
            Left = 20;
        }

        if (Top < 0)
        {
            Top = 20;
        }
    }

    private void StickerWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is System.Windows.Controls.Button)
        {
            return;
        }

        _isDragging = true;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void StickerWindow_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isDragging)
        {
            var currentPosition = e.GetPosition(null);
            Left += currentPosition.X - _dragStart.X;
            Top += currentPosition.Y - _dragStart.Y;
            e.Handled = true;
        }
    }

    private void StickerWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void StickerWindow_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        CloseButton.Opacity = 1;
        ResizeGrip.Opacity = 0.5;
    }

    private void StickerWindow_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (!_isDragging)
        {
            CloseButton.Opacity = 0;
            ResizeGrip.Opacity = 0;
        }
    }

    private void StickerWindow_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击关闭贴图
        StickerClosed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        StickerClosed?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void StickerWindow_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Delta > 0 = scroll up = zoom in; Delta < 0 = scroll down = zoom out
        var zoom = e.Delta > 0 ? ZoomFactor : 1.0 / ZoomFactor;
        var newScale = Math.Clamp(_scale * zoom, MinScale, MaxScale);

        if (Math.Abs(newScale - _scale) < 0.001)
        {
            return;
        }

        // Remember window center before zoom
        var centerX = Left + ActualWidth / 2;
        var centerY = Top + ActualHeight / 2;

        _scale = newScale;
        StickerImage.MaxWidth = _baseWidth * _scale;
        StickerImage.MaxHeight = _baseHeight * _scale;

        // UpdateLayout so SizeToContent recalculates the window size
        UpdateLayout();

        // Keep window centered on the same point
        Left = centerX - ActualWidth / 2;
        Top = centerY - ActualHeight / 2;

        e.Handled = true;
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            StickerClosed?.Invoke(this, EventArgs.Empty);
            Close();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
