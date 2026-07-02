using System.Windows;

namespace SnipEasy.App.Services;

public static class CaptureSelectionGeometry
{
    public const double MinimumSelectionSize = 12;

    public static Rect Clamp(Rect selection, double canvasWidth, double canvasHeight)
    {
        var width = Math.Clamp(selection.Width, 0, Math.Max(0, canvasWidth));
        var height = Math.Clamp(selection.Height, 0, Math.Max(0, canvasHeight));
        var left = Math.Clamp(selection.Left, 0, Math.Max(0, canvasWidth - width));
        var top = Math.Clamp(selection.Top, 0, Math.Max(0, canvasHeight - height));
        return new Rect(left, top, width, height);
    }

    public static bool IsValid(Rect selection)
    {
        return selection.Width >= MinimumSelectionSize && selection.Height >= MinimumSelectionSize;
    }

    public static Int32Rect ToPixelRegion(Rect region, double viewportWidth, double viewportHeight, int pixelWidth, int pixelHeight)
    {
        var scaleX = pixelWidth / Math.Max(1, viewportWidth);
        var scaleY = pixelHeight / Math.Max(1, viewportHeight);

        var x = (int)Math.Round(region.X * scaleX);
        var y = (int)Math.Round(region.Y * scaleY);
        var width = (int)Math.Round(region.Width * scaleX);
        var height = (int)Math.Round(region.Height * scaleY);

        x = Math.Clamp(x, 0, Math.Max(0, pixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, pixelHeight - 1));
        width = Math.Clamp(width, 1, Math.Max(1, pixelWidth - x));
        height = Math.Clamp(height, 1, Math.Max(1, pixelHeight - y));

        return new Int32Rect(x, y, width, height);
    }

    public static Rect Resize(Rect bounds, double horizontalChange, double verticalChange, string handle, double canvasWidth, double canvasHeight)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (handle.Contains("Left", StringComparison.OrdinalIgnoreCase))
        {
            left = Math.Clamp(left + horizontalChange, 0, right - MinimumSelectionSize);
        }

        if (handle.Contains("Right", StringComparison.OrdinalIgnoreCase))
        {
            right = Math.Clamp(right + horizontalChange, left + MinimumSelectionSize, Math.Max(0, canvasWidth));
        }

        if (handle.Contains("Top", StringComparison.OrdinalIgnoreCase))
        {
            top = Math.Clamp(top + verticalChange, 0, bottom - MinimumSelectionSize);
        }

        if (handle.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
        {
            bottom = Math.Clamp(bottom + verticalChange, top + MinimumSelectionSize, Math.Max(0, canvasHeight));
        }

        return new Rect(left, top, Math.Max(MinimumSelectionSize, right - left), Math.Max(MinimumSelectionSize, bottom - top));
    }
}
