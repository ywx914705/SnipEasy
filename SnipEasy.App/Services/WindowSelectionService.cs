using System.Drawing;
using System.Runtime.InteropServices;
using SnipEasy.App.Native;

namespace SnipEasy.App.Services;

public sealed class WindowSelectionService
{
    private const int MinimumWindowDimension = 24;

    public bool TryFindWindowAt(Point screenPoint, out WindowPixelBounds bounds)
    {
        var excludedProcessId = (uint)Environment.ProcessId;
        WindowPixelBounds? match = null;

        _ = NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!IsSelectableWindow(windowHandle, excludedProcessId) ||
                !TryGetBounds(windowHandle, out var candidate) ||
                !candidate.Contains(screenPoint))
            {
                return true;
            }

            match = candidate;
            return false;
        }, IntPtr.Zero);

        bounds = match ?? default;
        return match is not null;
    }

    private static bool IsSelectableWindow(IntPtr windowHandle, uint excludedProcessId)
    {
        if (windowHandle == IntPtr.Zero ||
            !NativeMethods.IsWindowVisible(windowHandle) ||
            NativeMethods.GetWindowTextLength(windowHandle) == 0)
        {
            return false;
        }

        _ = NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        if (processId == 0 || processId == excludedProcessId)
        {
            return false;
        }

        try
        {
            var result = NativeMethods.DwmGetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaCloaked,
                out int cloaked,
                Marshal.SizeOf<int>());
            return result != 0 || cloaked == 0;
        }
        catch (DllNotFoundException)
        {
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            return true;
        }
    }

    private static bool TryGetBounds(IntPtr windowHandle, out WindowPixelBounds bounds)
    {
        NativeMethods.RECT rect;
        var hasExtendedBounds = false;
        try
        {
            hasExtendedBounds = NativeMethods.DwmGetWindowAttribute(
                windowHandle,
                NativeMethods.DwmwaExtendedFrameBounds,
                out rect,
                Marshal.SizeOf<NativeMethods.RECT>()) == 0;
        }
        catch (DllNotFoundException)
        {
            rect = default;
        }
        catch (EntryPointNotFoundException)
        {
            rect = default;
        }

        if (!hasExtendedBounds && !NativeMethods.GetWindowRect(windowHandle, out rect))
        {
            bounds = default;
            return false;
        }

        bounds = new WindowPixelBounds(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return bounds.Width >= MinimumWindowDimension && bounds.Height >= MinimumWindowDimension;
    }
}

public readonly record struct WindowPixelBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);

    public int Height => Math.Max(0, Bottom - Top);

    public bool Contains(Point point)
    {
        return point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
    }
}
