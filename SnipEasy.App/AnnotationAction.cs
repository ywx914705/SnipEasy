using System.Windows;
using System.Windows.Ink;

namespace SnipEasy.App;

public enum AnnotationActionKind
{
    AddStroke,
    AddElement,
    DeleteElement,
    ChangeElementBounds
}

public sealed record AnnotationAction(AnnotationActionKind Kind, Stroke? Stroke, UIElement? Element, Rect? FromBounds, Rect? ToBounds, int ElementIndex)
{
    public static AnnotationAction AddStroke(Stroke stroke) => new(AnnotationActionKind.AddStroke, stroke, null, null, null, -1);
    public static AnnotationAction AddElement(UIElement element) => new(AnnotationActionKind.AddElement, null, element, null, null, -1);
    public static AnnotationAction DeleteElement(UIElement element, Rect bounds, int elementIndex) => new(AnnotationActionKind.DeleteElement, null, element, bounds, null, elementIndex);
    public static AnnotationAction ChangeElementBounds(UIElement element, Rect from, Rect to) => new(AnnotationActionKind.ChangeElementBounds, null, element, from, to, -1);
}
