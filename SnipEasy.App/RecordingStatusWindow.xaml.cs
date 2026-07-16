using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using SnipEasy.App.Native;
using Forms = System.Windows.Forms;

namespace SnipEasy.App;

public partial class RecordingStatusWindow : Window
{
    private readonly DateTimeOffset _startedAt;
    private readonly string _recordingSubtitle;
    private readonly DispatcherTimer _timer;
    private bool _stopRequested;
    private bool _isPaused;
    private DateTimeOffset? _pausedAt;
    private TimeSpan _pausedDuration;
    private int _lastSecond = -1;

    public RecordingStatusWindow(DateTimeOffset startedAt, string subtitle)
    {
        _startedAt = startedAt;
        _recordingSubtitle = string.IsNullOrWhiteSpace(subtitle) ? "" : subtitle.Trim();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += (_, _) => UpdateElapsed();

        InitializeComponent();
        ShowRecordingState();
    }

    public event EventHandler? StopRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;

    public void ShowStoppingState()
    {
        CommitPausedDuration();
        _stopRequested = true;
        _isPaused = false;
        _timer.Stop();
        StatusText.Text = "正在停止";
        RouteText.Text = "封装视频中...";
        ActionHintText.Text = "请稍候";
        StopButtonLabel.Text = "完成中";
        PauseButton.IsEnabled = false;
        PauseButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Visible;
        DecisionPanel.Visibility = Visibility.Collapsed;
    }

    public void ShowRecordingState(string? status = null)
    {
        CommitPausedDuration();
        _stopRequested = false;
        _isPaused = false;
        StatusText.Text = string.IsNullOrWhiteSpace(status) ? "录制中" : status;
        RouteText.Text = _recordingSubtitle;
        ActionHintText.Text = "F2 完成";
        PauseButtonLabel.Text = "暂停";
        PauseButtonIcon.Text = "\uE769";
        PauseButton.ToolTip = "暂停录屏";
        PauseButton.IsEnabled = true;
        PauseButton.Visibility = Visibility.Visible;
        RecordDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        RecordDotGlow.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
        StopButtonLabel.Text = "完成录制";
        StopButton.IsEnabled = true;
        StopButton.Visibility = Visibility.Visible;
        DecisionPanel.Visibility = Visibility.Collapsed;
        _timer.Start();
    }

    public void ShowPausedState()
    {
        if (_stopRequested)
        {
            return;
        }

        if (!_isPaused)
        {
            _pausedAt = DateTimeOffset.Now;
        }

        _isPaused = true;
        UpdateElapsed();
        _timer.Stop();
        StatusText.Text = "已暂停";
        RouteText.Text = "暂停期间不会写入视频";
        ActionHintText.Text = "可继续或完成";
        PauseButtonLabel.Text = "继续";
        PauseButtonIcon.Text = "\uE768";
        PauseButton.ToolTip = "继续录屏";
        PauseButton.IsEnabled = true;
        RecordDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        RecordDotGlow.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
        StopButton.IsEnabled = true;
    }

    public void ShowDecisionState(string fileName, string saveDirectory)
    {
        _timer.Stop();
        _stopRequested = true;
        StatusText.Text = "已完成";
        RouteText.Text = string.IsNullOrWhiteSpace(fileName) ? "选择保存或取消" : fileName;
        ActionHintText.Text = saveDirectory;
        PauseButton.Visibility = Visibility.Collapsed;
        StopButton.Visibility = Visibility.Collapsed;
        DecisionPanel.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = true;
        CancelButton.IsEnabled = true;
    }

    public void ShowSavingState()
    {
        StatusText.Text = "保存中";
        RouteText.Text = "写入视频目录...";
        ActionHintText.Text = "请稍候";
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
    }

    public void ShowDiscardingState()
    {
        StatusText.Text = "取消中";
        RouteText.Text = "删除录屏...";
        ActionHintText.Text = "请稍候";
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
    }

    private void RecordingStatusWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateElapsed();
        PlaceNearTopRight();
        Opacity = 1;
        _timer.Start();
    }

    private void RecordingStatusWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WdaExcludeFromCapture);
    }

    private void RecordingStatusWindow_Closed(object? sender, EventArgs e)
    {
        _timer.Stop();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stopRequested)
        {
            return;
        }

        ShowStoppingState();
        StopRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_stopRequested)
        {
            return;
        }

        PauseButton.IsEnabled = false;
        PauseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSavingState();
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDiscardingState();
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Chrome_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || HasButtonAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void UpdateElapsed()
    {
        var effectiveNow = _pausedAt ?? DateTimeOffset.Now;
        var elapsed = effectiveNow - _startedAt - _pausedDuration;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var totalSeconds = (int)elapsed.TotalSeconds;
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        // 时间显示
        var timeStr = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:00}:{minutes:00}:{seconds:00}"
            : $"{minutes:00}:{seconds:00}";

        // 秒数跳动
        if (seconds != _lastSecond)
        {
            ElapsedText.Text = timeStr;
            _lastSecond = seconds;

            // 触发秒数跳动动画
            var storyboard = (System.Windows.Media.Animation.Storyboard)FindResource("SecondTick");
            storyboard.Begin();
        }
    }

    private void CommitPausedDuration()
    {
        if (_pausedAt is null)
        {
            return;
        }

        _pausedDuration += DateTimeOffset.Now - _pausedAt.Value;
        _pausedAt = null;
    }

    private void PlaceNearTopRight()
    {
        UpdateLayout();

        var workingArea = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = transform.Transform(new System.Windows.Point(workingArea.Left, workingArea.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
        const double margin = 18;

        Left = Math.Max(topLeft.X + margin, bottomRight.X - ActualWidth - margin);
        Top = topLeft.Y + margin;
    }

    private static bool HasButtonAncestor(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}
