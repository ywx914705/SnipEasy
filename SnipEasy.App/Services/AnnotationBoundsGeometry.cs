using System.Windows;

namespace SnipEasy.App.Services;

public static class AnnotationBoundsGeometry
{
    public const double MinimumElementSize = 12;

    public static Rect Resize(Rect bounds, double horizontalChange, double verticalChange, string handle, double canvasWidth, double canvasHeight)
    {
        var left = bounds.Left;
        var top = bounds.Top;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        if (handle.Contains("Left", StringComparison.OrdinalIgnoreCase))
        {
            left = Math.Clamp(left + horizontalChange, 0, right - MinimumElementSize);
        }

        if (handle.Contains("Right", StringComparison.OrdinalIgnoreCase))
        {
            right = Math.Clamp(right + horizontalChange, left + MinimumElementSize, Math.Max(0, canvasWidth));
        }

        if (handle.Contains("Top", StringComparison.OrdinalIgnoreCase))
        {
            top = Math.Clamp(top + verticalChange, 0, bottom - MinimumElementSize);
        }

        if (handle.Contains("Bottom", StringComparison.OrdinalIgnoreCase))
        {
            bottom = Math.Clamp(bottom + verticalChange, top + MinimumElementSize, Math.Max(0, canvasHeight));
        }

        return new Rect(left, top, Math.Max(MinimumElementSize, right - left), Math.Max(MinimumElementSize, bottom - top));
    }
}
