using System.Windows;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace SnipEasy.App.Services;

/// <summary>
/// Manages selection state for region capture.
/// Delegates geometry calculations to <see cref="CaptureSelectionGeometry"/>.
/// </summary>
public sealed class SelectionManager
{
    private WpfRect _selection;
    private WpfPoint _dragStart;
    private WpfPoint _moveStart;
    private WpfRect _moveStartSelection;

    /// <summary>
    /// Gets the current selection rectangle.
    /// </summary>
    public WpfRect Selection => _selection;

    /// <summary>
    /// Gets whether there is a valid selection.
    /// </summary>
    public bool HasValidSelection => CaptureSelectionGeometry.IsValid(_selection);

    /// <summary>
    /// Event raised when the selection changes.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Starts drawing a new selection from the specified point.
    /// </summary>
    public void StartDrawing(WpfPoint start)
    {
        _dragStart = start;
        _selection = new WpfRect(start, start);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates the selection during drawing.
    /// </summary>
    public void UpdateDrawing(WpfPoint current, double maxWidth, double maxHeight)
    {
        var left = Math.Min(_dragStart.X, current.X);
        var top = Math.Min(_dragStart.Y, current.Y);
        var right = Math.Max(_dragStart.X, current.X);
        var bottom = Math.Max(_dragStart.Y, current.Y);

        _selection = CaptureSelectionGeometry.Clamp(
            new WpfRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)),
            maxWidth,
            maxHeight);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Starts moving the current selection.
    /// </summary>
    public void StartMoving(WpfPoint start)
    {
        _moveStart = start;
        _moveStartSelection = _selection;
    }

    /// <summary>
    /// Updates the selection position during moving.
    /// </summary>
    public void UpdateMoving(WpfPoint current, double maxWidth, double maxHeight)
    {
        var delta = current - _moveStart;
        _selection = CaptureSelectionGeometry.Clamp(
            new WpfRect(
                _moveStartSelection.Left + delta.X,
                _moveStartSelection.Top + delta.Y,
                _moveStartSelection.Width,
                _moveStartSelection.Height),
            maxWidth,
            maxHeight);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resizes the selection from a specific handle.
    /// </summary>
    public void Resize(WpfRect startBounds, double horizontalChange, double verticalChange, string handle, double maxWidth, double maxHeight)
    {
        _selection = CaptureSelectionGeometry.Resize(startBounds, horizontalChange, verticalChange, handle, maxWidth, maxHeight);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Nudges the selection by the specified amount.
    /// </summary>
    public void Nudge(double deltaX, double deltaY, double maxWidth, double maxHeight)
    {
        _selection = CaptureSelectionGeometry.Clamp(
            new WpfRect(
                _selection.Left + deltaX,
                _selection.Top + deltaY,
                _selection.Width,
                _selection.Height),
            maxWidth,
            maxHeight);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Resets the selection to empty.
    /// </summary>
    public void Reset()
    {
        _selection = WpfRect.Empty;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Converts the selection rectangle to pixel coordinates.
    /// </summary>
    public Int32Rect ToPixelRegion(double scaleX, double scaleY, int maxWidth, int maxHeight)
    {
        var x = (int)Math.Round(_selection.X * scaleX);
        var y = (int)Math.Round(_selection.Y * scaleY);
        var width = (int)Math.Round(_selection.Width * scaleX);
        var height = (int)Math.Round(_selection.Height * scaleY);

        x = Math.Clamp(x, 0, maxWidth - 1);
        y = Math.Clamp(y, 0, maxHeight - 1);
        width = Math.Clamp(width, 1, maxWidth - x);
        height = Math.Clamp(height, 1, maxHeight - y);

        return new Int32Rect(x, y, width, height);
    }
}
