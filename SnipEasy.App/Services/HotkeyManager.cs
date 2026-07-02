using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using SnipEasy.App.Native;

namespace SnipEasy.App.Services;

public sealed class HotkeyManager : IDisposable
{
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMilliseconds(350);

    private readonly Dictionary<int, HotkeyRegistration> _actions = new();
    private readonly List<int> _registeredIds = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly Dictionary<int, DateTimeOffset> _lastFiredAt = new();
    private readonly object _gate = new();
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private IntPtr _hookHandle;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private SynchronizationContext? _context;
    private bool _disposed;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? RegistrationFailed;

    public void Attach(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
        _context = SynchronizationContext.Current;
        _source = HwndSource.FromHwnd(windowHandle);
        _source?.AddHook(WndProc);
        InstallKeyboardHook();
    }

    public void Register(int id, Key key, uint modifiers, string label, Action action)
    {
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        var flags = modifiers | NativeMethods.ModNoRepeat;
        var registration = new HotkeyRegistration(id, virtualKey, modifiers, label, action);
        _actions[id] = registration;

        if (NativeMethods.RegisterHotKey(_windowHandle, id, flags, virtualKey))
        {
            _registeredIds.Add(id);
            StatusChanged?.Invoke(this, $"{label} 系统热键已注册。");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            RegistrationFailed?.Invoke(this, $"{label} 系统热键注册失败，已启用键盘钩子兜底。系统错误码 {error}。");
        }
    }

    /// <summary>
    /// Registers a hotkey from a string representation (e.g., "F1", "Ctrl+Shift+S").
    /// </summary>
    public bool RegisterFromString(int id, string hotkeyString, string label, Action action)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return false;
        }

        if (!TryParseHotkey(hotkeyString, out var key, out var modifiers))
        {
            RegistrationFailed?.Invoke(this, $"无法解析热键：{hotkeyString}");
            return false;
        }

        Register(id, key, modifiers, label, action);
        return true;
    }

    /// <summary>
    /// Unregisters a hotkey by ID.
    /// </summary>
    public void Unregister(int id)
    {
        if (_registeredIds.Contains(id))
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, id);
            _registeredIds.Remove(id);
        }

        _actions.Remove(id);
    }

    /// <summary>
    /// Parses a hotkey string into Key and modifiers.
    /// </summary>
    public static bool TryParseHotkey(string hotkeyString, out Key key, out uint modifiers)
    {
        key = Key.None;
        modifiers = 0;

        if (string.IsNullOrWhiteSpace(hotkeyString))
        {
            return false;
        }

        var parts = hotkeyString.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToLowerInvariant();
            switch (modifier)
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.ModControl;
                    break;
                case "alt":
                    modifiers |= NativeMethods.ModAlt;
                    break;
                case "shift":
                    modifiers |= NativeMethods.ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.ModWin;
                    break;
                default:
                    return false;
            }
        }

        var keyName = parts[^1];
        key = keyName.ToUpperInvariant() switch
        {
            "F1" => Key.F1,
            "F2" => Key.F2,
            "F3" => Key.F3,
            "F4" => Key.F4,
            "F5" => Key.F5,
            "F6" => Key.F6,
            "F7" => Key.F7,
            "F8" => Key.F8,
            "F9" => Key.F9,
            "F10" => Key.F10,
            "F11" => Key.F11,
            "F12" => Key.F12,
            "SPACE" => Key.Space,
            "ENTER" => Key.Enter,
            "ESCAPE" => Key.Escape,
            "TAB" => Key.Tab,
            "BACKSPACE" => Key.Back,
            "DELETE" => Key.Delete,
            "INSERT" => Key.Insert,
            "HOME" => Key.Home,
            "END" => Key.End,
            "PAGEUP" => Key.PageUp,
            "PAGEDOWN" => Key.PageDown,
            "UP" => Key.Up,
            "DOWN" => Key.Down,
            "LEFT" => Key.Left,
            "RIGHT" => Key.Right,
            _ => Enum.TryParse<Key>(keyName, true, out var parsed) ? parsed : Key.None
        };

        return key != Key.None;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var id in _registeredIds)
        {
            _ = NativeMethods.UnregisterHotKey(_windowHandle, id);
        }

        _registeredIds.Clear();
        _source?.RemoveHook(WndProc);

        if (_hookHandle != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _disposed = true;
    }

    private void InstallKeyboardHook()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        _hookProc = KeyboardHookProc;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _hookProc, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            RegistrationFailed?.Invoke(this, $"键盘钩子安装失败，系统错误码 {error}。");
        }
        else
        {
            StatusChanged?.Invoke(this, "键盘钩子兜底已启用。");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var registration))
        {
            handled = true;
            Fire(registration);
        }

        return IntPtr.Zero;
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var keyInfo = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
        var key = keyInfo.VkCode;

        if (message is NativeMethods.WmKeyup or NativeMethods.WmSyskeyup)
        {
            lock (_gate)
            {
                _pressedKeys.Remove(key);
            }

            return IsRegisteredKey(key)
                ? new IntPtr(1)
                : NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (message is not (NativeMethods.WmKeydown or NativeMethods.WmSyskeydown))
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        HotkeyRegistration? registration = null;
        lock (_gate)
        {
            var isRegisteredKey = _actions.Values.Any(item => item.VirtualKey == key);
            if (!isRegisteredKey)
            {
                return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (!_pressedKeys.Add(key))
            {
                return new IntPtr(1);
            }

            var modifiers = GetActiveModifiers();
            registration = _actions.Values.FirstOrDefault(item =>
                item.VirtualKey == key && NormalizeModifiers(item.Modifiers) == modifiers);
        }

        if (registration is null)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        Fire(registration);
        return new IntPtr(1);
    }

    private bool IsRegisteredKey(uint virtualKey)
    {
        lock (_gate)
        {
            return _actions.Values.Any(item => item.VirtualKey == virtualKey);
        }
    }

    private void Fire(HotkeyRegistration registration)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_lastFiredAt.TryGetValue(registration.Id, out var last) && now - last < DuplicateWindow)
            {
                return;
            }

            _lastFiredAt[registration.Id] = now;
        }

        if (_context is not null)
        {
            _context.Post(_ => registration.Action(), null);
        }
        else
        {
            registration.Action();
        }
    }

    private static uint GetActiveModifiers()
    {
        uint modifiers = 0;
        if (IsKeyDown(NativeMethods.VkControl))
        {
            modifiers |= NativeMethods.ModControl;
        }

        if (IsKeyDown(NativeMethods.VkMenu))
        {
            modifiers |= NativeMethods.ModAlt;
        }

        if (IsKeyDown(NativeMethods.VkShift))
        {
            modifiers |= NativeMethods.ModShift;
        }

        if (IsKeyDown(NativeMethods.VkLwin) || IsKeyDown(NativeMethods.VkRwin))
        {
            modifiers |= NativeMethods.ModWin;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKey)
    {
        return (NativeMethods.GetKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }

    private static uint NormalizeModifiers(uint modifiers)
    {
        return modifiers & (NativeMethods.ModAlt | NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModWin);
    }

    private sealed record HotkeyRegistration(int Id, uint VirtualKey, uint Modifiers, string Label, Action Action);
}
