using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SnipEasy.App.Services;

public sealed class OcrService
{
    public async Task<OcrRecognitionResult> RecognizeAsync(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                return OcrRecognitionResult.Failure(
                    "Windows OCR 不可用，请在系统语言设置中安装至少一个 OCR 语言包。");
            }

            var normalizedSource = NormalizeImageSize(source);
            var pngBytes = EncodePng(normalizedSource);

            using var randomAccessStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(randomAccessStream))
            {
                writer.WriteBytes(pngBytes);
                _ = await writer.StoreAsync();
                _ = await writer.FlushAsync();
                writer.DetachStream();
            }

            randomAccessStream.Seek(0);
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied);
            var result = await engine.RecognizeAsync(softwareBitmap);
            var text = result.Text?.Trim() ?? string.Empty;

            return string.IsNullOrWhiteSpace(text)
                ? OcrRecognitionResult.Failure("没有在所选区域中识别到文字。")
                : OcrRecognitionResult.Success(text);
        }
        catch (Exception ex)
        {
            return OcrRecognitionResult.Failure($"文字识别失败：{ex.Message}");
        }
    }

    internal static BitmapSource NormalizeImageSize(BitmapSource source)
    {
        var maximumDimension = Math.Max(1, OcrEngine.MaxImageDimension);
        var largestDimension = Math.Max(source.PixelWidth, source.PixelHeight);
        if (largestDimension <= maximumDimension)
        {
            return source;
        }

        var scale = maximumDimension / (double)largestDimension;
        var transformed = new TransformedBitmap(
            source,
            new ScaleTransform(scale, scale));
        transformed.Freeze();
        return transformed;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

public sealed record OcrRecognitionResult(bool IsSuccessful, string Text, string Message)
{
    public static OcrRecognitionResult Success(string text)
    {
        return new OcrRecognitionResult(true, text, "文字已识别并复制到剪贴板。");
    }

    public static OcrRecognitionResult Failure(string message)
    {
        return new OcrRecognitionResult(false, string.Empty, message);
    }
}
