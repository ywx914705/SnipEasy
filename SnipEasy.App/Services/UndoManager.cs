using System.Windows;
using System.Windows.Ink;

namespace SnipEasy.App.Services;

/// <summary>
/// Manages undo operations for annotation actions.
/// Delegates action representation to <see cref="AnnotationAction"/>.
/// </summary>
public sealed class UndoManager
{
    private readonly Stack<AnnotationAction> _undoStack = new();

    /// <summary>
    /// Gets whether there are actions to undo.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Pushes a stroke addition action.
    /// </summary>
    public void PushStroke(Stroke stroke)
    {
        _undoStack.Push(AnnotationAction.AddStroke(stroke));
    }

    /// <summary>
    /// Pushes an element addition action.
    /// </summary>
    public void PushElement(UIElement element)
    {
        _undoStack.Push(AnnotationAction.AddElement(element));
    }

    /// <summary>
    /// Pushes an element bounds change action.
    /// </summary>
    public void PushBoundsChange(UIElement element, Rect fromBounds, Rect toBounds)
    {
        if (Math.Abs(toBounds.Left - fromBounds.Left) > 0.5 ||
            Math.Abs(toBounds.Top - fromBounds.Top) > 0.5 ||
            Math.Abs(toBounds.Width - fromBounds.Width) > 0.5 ||
            Math.Abs(toBounds.Height - fromBounds.Height) > 0.5)
        {
            _undoStack.Push(AnnotationAction.ChangeElementBounds(element, fromBounds, toBounds));
        }
    }

    /// <summary>
    /// Undoes the last action and returns it, or null if the stack is empty.
    /// </summary>
    public AnnotationAction? Undo()
    {
        if (_undoStack.Count == 0)
        {
            return null;
        }

        return _undoStack.Pop();
    }

    /// <summary>
    /// Clears all undo actions.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
    }
}
