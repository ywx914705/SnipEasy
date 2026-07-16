using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnipEasy.App.Services;

public sealed class ClipboardService
{
    public void SetImage(BitmapSource image)
    {
        RetryClipboard(() => System.Windows.Clipboard.SetImage(image));
    }

    public void SetFileDrop(string filePath)
    {
        var files = new StringCollection { filePath };
        RetryClipboard(() => System.Windows.Clipboard.SetFileDropList(files));
    }

    public void SetText(string text)
    {
        RetryClipboard(() => System.Windows.Clipboard.SetText(text));
    }

    private static void RetryClipboard(Action action)
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(80);
            }
        }

        throw new InvalidOperationException("剪贴板暂时被其他程序占用。", lastError);
    }
}
