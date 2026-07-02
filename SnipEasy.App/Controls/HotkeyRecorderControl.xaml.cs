using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfRoutedEventArgs = System.Windows.RoutedEventArgs;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace SnipEasy.App.Controls;

/// <summary>
/// A control that allows users to record custom hotkey combinations.
/// Click to start recording, press desired key combination, click again to stop.
/// </summary>
public partial class HotkeyRecorderControl : WpfUserControl
{
    private bool _isRecording;
    private string _currentHotkey = string.Empty;

    public HotkeyRecorderControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets the recorded hotkey string (e.g., "F1", "Ctrl+Shift+S").
    /// </summary>
    public string Hotkey
    {
        get => _currentHotkey;
        set
        {
            _currentHotkey = value ?? string.Empty;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Event raised when the hotkey changes.
    /// </summary>
    public event EventHandler<string>? HotkeyChanged;

    /// <summary>
    /// Gets whether the control is currently recording a hotkey.
    /// </summary>
    public bool IsRecording => _isRecording;

    private void HotkeyBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        Hotkey = string.Empty;
        HotkeyChanged?.Invoke(this, Hotkey);
    }

    private void StartRecording()
    {
        _isRecording = true;
        HotkeyText.Text = "请按下快捷键...";
        HotkeyText.Foreground = System.Windows.Media.Brushes.Red;
        HotkeyBorder.BorderBrush = System.Windows.Media.Brushes.Red;
        Focus();
    }

    private void StopRecording()
    {
        _isRecording = false;
        UpdateDisplay();
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        if (!_isRecording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;

        // Ignore modifier keys alone
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or
            Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or
            Key.LWin or Key.RWin)
        {
            return;
        }

        // Build hotkey string
        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        // Get key name
        var keyName = e.Key switch
        {
            Key.F1 => "F1",
            Key.F2 => "F2",
            Key.F3 => "F3",
            Key.F4 => "F4",
            Key.F5 => "F5",
            Key.F6 => "F6",
            Key.F7 => "F7",
            Key.F8 => "F8",
            Key.F9 => "F9",
            Key.F10 => "F10",
            Key.F11 => "F11",
            Key.F12 => "F12",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Escape => "Escape",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            _ => e.Key.ToString()
        };

        parts.Add(keyName);
        Hotkey = string.Join("+", parts);
        HotkeyChanged?.Invoke(this, Hotkey);
        StopRecording();
    }

    protected override void OnLostFocus(WpfRoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording();
        }
        base.OnLostFocus(e);
    }

    private void UpdateDisplay()
    {
        if (string.IsNullOrWhiteSpace(_currentHotkey))
        {
            HotkeyText.Text = "点击录制热键";
            HotkeyText.Foreground = System.Windows.Media.Brushes.Gray;
            HotkeyBorder.BorderBrush = System.Windows.Media.Brushes.LightGray;
            ClearButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            HotkeyText.Text = _currentHotkey;
            HotkeyText.Foreground = System.Windows.Media.Brushes.Black;
            HotkeyBorder.BorderBrush = System.Windows.Media.Brushes.Gray;
            ClearButton.Visibility = Visibility.Visible;
        }
    }
}
