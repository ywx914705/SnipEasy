using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnipEasy.App.Services;

public static class CaptureImageRenderer
{
    public static BitmapSource Render(FrameworkElement captureSurface, Int32Rect pixelRegion)
    {
        captureSurface.UpdateLayout();
        var scaleX = pixelRegion.Width / Math.Max(1, captureSurface.ActualWidth);
        var scaleY = pixelRegion.Height / Math.Max(1, captureSurface.ActualHeight);
        var renderTarget = new RenderTargetBitmap(pixelRegion.Width, pixelRegion.Height, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32);
        renderTarget.Render(captureSurface);
        renderTarget.Freeze();
        return renderTarget;
    }
}
