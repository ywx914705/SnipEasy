using System.Windows.Media.Imaging;

namespace SnipEasy.App.Models;

public sealed class CaptureResult
{
    public required CaptureRecord Record { get; init; }
    public BitmapSource? Image { get; init; }
}
