using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace SnipEasy.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayService(
        Action showWindow,
        Action captureScreenshot,
        Action toggleRecording,
        Action openSaveDirectory,
        Action exitApplication)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? AppContext.BaseDirectory) ?? Drawing.SystemIcons.Application,
            Text = "SnipEasy",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("打开 SnipEasy", null, (_, _) => showWindow());
        _notifyIcon.ContextMenuStrip.Items.Add("F1 截图", null, (_, _) => captureScreenshot());
        _notifyIcon.ContextMenuStrip.Items.Add("F2 开始/停止录屏", null, (_, _) => toggleRecording());
        _notifyIcon.ContextMenuStrip.Items.Add("打开保存目录", null, (_, _) => openSaveDirectory());
        _notifyIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => exitApplication());
        _notifyIcon.DoubleClick += (_, _) => showWindow();
    }

    public void ShowTip(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
