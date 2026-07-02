using System.Windows.Input;
using SnipEasy.App.Native;
using SnipEasy.App.Services;

namespace SnipEasy.App.Tests;

[Trait("Category", "Unit")]
public class HotkeyManagerTests
{
    [Theory]
    [InlineData("F1", Key.F1, 0u)]
    [InlineData("F2", Key.F2, 0u)]
    [InlineData("F12", Key.F12, 0u)]
    [InlineData("SPACE", Key.Space, 0u)]
    [InlineData("ENTER", Key.Enter, 0u)]
    [InlineData("ESCAPE", Key.Escape, 0u)]
    public void TryParseHotkey_SingleKey_ReturnsCorrectKey(string input, Key expectedKey, uint expectedModifiers)
    {
        var result = HotkeyManager.TryParseHotkey(input, out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedModifiers, modifiers);
    }

    [Fact]
    public void TryParseHotkey_CtrlS_ReturnsCorrectModifiers()
    {
        var result = HotkeyManager.TryParseHotkey("Ctrl+S", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.S, key);
        Assert.Equal(NativeMethods.ModControl, modifiers);
    }

    [Fact]
    public void TryParseHotkey_CtrlShiftS_ReturnsMultipleModifiers()
    {
        var result = HotkeyManager.TryParseHotkey("Ctrl+Shift+S", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.S, key);
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModShift, modifiers);
    }

    [Fact]
    public void TryParseHotkey_AltF4_ReturnsAltModifier()
    {
        var result = HotkeyManager.TryParseHotkey("Alt+F4", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.F4, key);
        Assert.Equal(NativeMethods.ModAlt, modifiers);
    }

    [Fact]
    public void TryParseHotkey_WinSpace_ReturnsWinModifier()
    {
        var result = HotkeyManager.TryParseHotkey("Win+Space", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.Space, key);
        Assert.Equal(NativeMethods.ModWin, modifiers);
    }

    [Fact]
    public void TryParseHotkey_CtrlAltShiftF12_ReturnsAllModifiers()
    {
        var result = HotkeyManager.TryParseHotkey("Ctrl+Alt+Shift+F12", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.F12, key);
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModShift, modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParseHotkey_EmptyOrNull_ReturnsFalse(string? input)
    {
        var result = HotkeyManager.TryParseHotkey(input ?? "", out var key, out var modifiers);

        Assert.False(result);
        Assert.Equal(Key.None, key);
        Assert.Equal(0u, modifiers);
    }

    [Fact]
    public void TryParseHotkey_InvalidModifier_ReturnsFalse()
    {
        var result = HotkeyManager.TryParseHotkey("Invalid+S", out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParseHotkey_InvalidKey_ReturnsFalse()
    {
        var result = HotkeyManager.TryParseHotkey("Ctrl+InvalidKey", out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryParseHotkey_CaseInsensitive_Modifier()
    {
        var result = HotkeyManager.TryParseHotkey("CTRL+f1", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.F1, key);
        Assert.Equal(NativeMethods.ModControl, modifiers);
    }

    [Fact]
    public void TryParseHotkey_WithSpaces_TrimsCorrectly()
    {
        var result = HotkeyManager.TryParseHotkey("  Ctrl + Shift + S  ", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.S, key);
        Assert.Equal(NativeMethods.ModControl | NativeMethods.ModShift, modifiers);
    }

    [Fact]
    public void TryParseHotkey_ControlWord_Works()
    {
        var result = HotkeyManager.TryParseHotkey("Control+S", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.S, key);
        Assert.Equal(NativeMethods.ModControl, modifiers);
    }

    [Fact]
    public void TryParseHotkey_WindowsWord_Works()
    {
        var result = HotkeyManager.TryParseHotkey("Windows+S", out var key, out var modifiers);

        Assert.True(result);
        Assert.Equal(Key.S, key);
        Assert.Equal(NativeMethods.ModWin, modifiers);
    }
}
