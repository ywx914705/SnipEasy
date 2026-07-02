using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace SnipEasy.App;

public partial class AnnotationWindow : Window
{
    private enum AnnotationTool
    {
        Select,
        Pen,
        Rectangle,
        Ellipse,
        Text
    }

    private enum AnnotationActionKind
    {
        AddStroke,
        AddElement,
        MoveElement
    }

    private readonly BitmapSource _sourceImage;
    private readonly Stack<AnnotationAction> _undoStack = new();
    private AnnotationTool _activeTool = AnnotationTool.Pen;
    private WpfColor _strokeColor = WpfColor.FromRgb(239, 68, 68);
    private double _strokeThickness = 4;
    private WpfPoint _shapeStart;
    private Shape? _draftShape;
    private UIElement? _selectedElement;
    private UIElement? _movingElement;
    private WpfPoint _elementDragStart;
    private WpfPoint _elementStartPosition;

    public BitmapSource? AnnotatedImage { get; private set; }
    public bool CopyToClipboard { get; private set; }
    public bool IsWatermarkApplied => WatermarkCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(WatermarkPreviewText.Text);

    public AnnotationWindow(BitmapSource sourceImage, bool watermarkEnabled, string watermarkText)
    {
        _sourceImage = sourceImage;
        InitializeComponent();

        // Convert pixel dimensions to DIPs for correct Viewbox scaling
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dipWidth = _sourceImage.PixelWidth / dpi;
        var dipHeight = _sourceImage.PixelHeight / dpi;

        CapturedImage.Source = _sourceImage;
        CaptureSurface.Width = dipWidth;
        CaptureSurface.Height = dipHeight;
        CapturedImage.Width = dipWidth;
        CapturedImage.Height = dipHeight;
        AnnotationCanvas.Width = dipWidth;
        AnnotationCanvas.Height = dipHeight;
        SelectionOverlay.Width = dipWidth;
        SelectionOverlay.Height = dipHeight;
        ImageInfoText.Text = $"{_sourceImage.PixelWidth} x {_sourceImage.PixelHeight} PNG";

        WatermarkCheckBox.IsChecked = watermarkEnabled;
        WatermarkTextBox.Text = watermarkText;

        SetStrokeColor(_strokeColor);
        SetActiveTool(AnnotationTool.Pen);
        UpdateDrawingAttributes();
        UpdateWatermarkPreview();
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            return;
        }

        if (e.Key == Key.Delete && _selectedElement is not null)
        {
            DeleteSelectedElement();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            UndoLastAction();
            e.Handled = true;
            return;
        }

        if ((e.Key is Key.Enter or Key.Return) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Finish(copyToClipboard: true);
            e.Handled = true;
            return;
        }

        if (TryNudgeSelectedElement(e.Key))
        {
            e.Handled = true;
        }
    }

    private void SelectTool_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(AnnotationTool.Select);
    }

    private void PenTool_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(AnnotationTool.Pen);
    }

    private void RectangleTool_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(AnnotationTool.Rectangle);
    }

    private void EllipseTool_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(AnnotationTool.Ellipse);
    }

    private void TextTool_Click(object sender, RoutedEventArgs e)
    {
        SetActiveTool(AnnotationTool.Text);
    }

    private void RedColor_Click(object sender, RoutedEventArgs e)
    {
        SetStrokeColor(WpfColor.FromRgb(239, 68, 68));
    }

    private void BlueColor_Click(object sender, RoutedEventArgs e)
    {
        SetStrokeColor(WpfColor.FromRgb(37, 99, 235));
    }

    private void AmberColor_Click(object sender, RoutedEventArgs e)
    {
        SetStrokeColor(WpfColor.FromRgb(245, 158, 11));
    }

    private void GreenColor_Click(object sender, RoutedEventArgs e)
    {
        SetStrokeColor(WpfColor.FromRgb(16, 185, 129));
    }

    private void BlackColor_Click(object sender, RoutedEventArgs e)
    {
        SetStrokeColor(WpfColor.FromRgb(17, 24, 39));
    }

    private void StrokeThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeThickness = e.NewValue;
        UpdateDrawingAttributes();
    }

    private void AnnotationCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        ClearSelectedElement();
        _undoStack.Push(AnnotationAction.AddStroke(e.Stroke));
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool is AnnotationTool.Pen)
        {
            ClearSelectedElement();
            return;
        }

        if (_activeTool is AnnotationTool.Select)
        {
            ClearSelectedElement();
            return;
        }

        var position = e.GetPosition(AnnotationCanvas);
        if (_activeTool is AnnotationTool.Text)
        {
            AddTextAnnotation(position);
            e.Handled = true;
            return;
        }

        ClearSelectedElement();
        _shapeStart = position;
        _draftShape = CreateShape();
        AnnotationCanvas.Children.Add(_draftShape);
        InkCanvas.SetLeft(_draftShape, _shapeStart.X);
        InkCanvas.SetTop(_draftShape, _shapeStart.Y);
        AnnotationCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void AnnotationCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draftShape is null || !AnnotationCanvas.IsMouseCaptured)
        {
            return;
        }

        UpdateDraftShape(e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draftShape is null || _activeTool is AnnotationTool.Pen or AnnotationTool.Select or AnnotationTool.Text)
        {
            return;
        }

        UpdateDraftShape(e.GetPosition(AnnotationCanvas));
        AnnotationCanvas.ReleaseMouseCapture();

        if (_draftShape.Width < 5 || _draftShape.Height < 5)
        {
            AnnotationCanvas.Children.Remove(_draftShape);
        }
        else
        {
            AttachMovableElement(_draftShape);
            SelectElement(_draftShape);
            _undoStack.Push(AnnotationAction.AddElement(_draftShape));
        }

        _draftShape = null;
        e.Handled = true;
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        UndoLastAction();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
        ClearSelectedElement();
        _undoStack.Clear();
    }

    private void Watermark_Changed(object sender, RoutedEventArgs e)
    {
        UpdateWatermarkPreview();
    }

    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWatermarkPreview();
    }

    private void SaveOnly_Click(object sender, RoutedEventArgs e)
    {
        Finish(copyToClipboard: false);
    }

    private void CopyAndSave_Click(object sender, RoutedEventArgs e)
    {
        Finish(copyToClipboard: true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Finish(bool copyToClipboard)
    {
        ClearSelectedElement();
        AnnotatedImage = RenderAnnotatedImage();
        CopyToClipboard = copyToClipboard;
        DialogResult = true;
        Close();
    }

    private void SetActiveTool(AnnotationTool tool)
    {
        _activeTool = tool;
        AnnotationCanvas.EditingMode = tool == AnnotationTool.Pen
            ? InkCanvasEditingMode.Ink
            : InkCanvasEditingMode.None;
        AnnotationCanvas.Cursor = tool switch
        {
            AnnotationTool.Select => WpfCursors.Arrow,
            AnnotationTool.Pen => WpfCursors.Pen,
            _ => WpfCursors.Cross
        };

        SetToolButtonState(SelectToolButton, tool == AnnotationTool.Select);
        SetToolButtonState(PenToolButton, tool == AnnotationTool.Pen);
        SetToolButtonState(RectangleToolButton, tool == AnnotationTool.Rectangle);
        SetToolButtonState(EllipseToolButton, tool == AnnotationTool.Ellipse);
        SetToolButtonState(TextToolButton, tool == AnnotationTool.Text);
    }

    private void SetStrokeColor(WpfColor color)
    {
        _strokeColor = color;
        UpdateDrawingAttributes();
        SetColorButtonState(RedColorButton, color == WpfColor.FromRgb(239, 68, 68));
        SetColorButtonState(BlueColorButton, color == WpfColor.FromRgb(37, 99, 235));
        SetColorButtonState(AmberColorButton, color == WpfColor.FromRgb(245, 158, 11));
        SetColorButtonState(GreenColorButton, color == WpfColor.FromRgb(16, 185, 129));
        SetColorButtonState(BlackColorButton, color == WpfColor.FromRgb(17, 24, 39));
    }

    private void UpdateDrawingAttributes()
    {
        if (AnnotationCanvas is null)
        {
            return;
        }

        AnnotationCanvas.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _strokeColor,
            Width = _strokeThickness,
            Height = _strokeThickness,
            FitToCurve = true,
            IgnorePressure = true
        };
    }

    private Shape CreateShape()
    {
        Shape shape = _activeTool == AnnotationTool.Rectangle
            ? new WpfRectangle()
            : new Ellipse();

        shape.Stroke = new SolidColorBrush(_strokeColor);
        shape.StrokeThickness = _strokeThickness;
        shape.Fill = WpfBrushes.Transparent;
        shape.SnapsToDevicePixels = true;
        return shape;
    }

    private void AddTextAnnotation(WpfPoint position)
    {
        ClearSelectedElement();

        var textBox = new System.Windows.Controls.TextBox
        {
            Text = "输入文字",
            Foreground = new SolidColorBrush(_strokeColor),
            Background = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            MinWidth = 120,
            AcceptsReturn = false
        };

        var container = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromArgb(230, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(_strokeColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Child = textBox
        };

        AnnotationCanvas.Children.Add(container);
        AttachMovableElement(container);
        SetElementPosition(container, position);
        SelectElement(container);
        _undoStack.Push(AnnotationAction.AddElement(container));

        textBox.Focus();
        textBox.SelectAll();
    }

    private void UpdateDraftShape(WpfPoint current)
    {
        if (_draftShape is null)
        {
            return;
        }

        var left = Math.Min(_shapeStart.X, current.X);
        var top = Math.Min(_shapeStart.Y, current.Y);
        var right = Math.Max(_shapeStart.X, current.X);
        var bottom = Math.Max(_shapeStart.Y, current.Y);

        left = Math.Clamp(left, 0, AnnotationCanvas.ActualWidth);
        top = Math.Clamp(top, 0, AnnotationCanvas.ActualHeight);
        right = Math.Clamp(right, 0, AnnotationCanvas.ActualWidth);
        bottom = Math.Clamp(bottom, 0, AnnotationCanvas.ActualHeight);

        InkCanvas.SetLeft(_draftShape, left);
        InkCanvas.SetTop(_draftShape, top);
        _draftShape.Width = Math.Max(0, right - left);
        _draftShape.Height = Math.Max(0, bottom - top);
    }

    private void UndoLastAction()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var action = _undoStack.Pop();
        if (action.Kind == AnnotationActionKind.AddStroke && action.Stroke is not null)
        {
            AnnotationCanvas.Strokes.Remove(action.Stroke);
            return;
        }

        if (action.Kind == AnnotationActionKind.AddElement && action.Element is not null)
        {
            if (_selectedElement == action.Element)
            {
                ClearSelectedElement();
            }

            AnnotationCanvas.Children.Remove(action.Element);
            return;
        }

        if (action.Kind == AnnotationActionKind.MoveElement && action.Element is not null && action.From is not null)
        {
            SetElementPosition(action.Element, action.From.Value);
            if (_selectedElement == action.Element)
            {
                UpdateSelectionFrame(action.Element);
            }
        }
    }

    private void AttachMovableElement(UIElement element)
    {
        element.MouseLeftButtonDown += AnnotationElement_MouseLeftButtonDown;
        element.MouseMove += AnnotationElement_MouseMove;
        element.MouseLeftButtonUp += AnnotationElement_MouseLeftButtonUp;
    }

    private void AnnotationElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || _draftShape is not null)
        {
            return;
        }

        SelectElement(element);
        _movingElement = element;
        _elementDragStart = e.GetPosition(AnnotationCanvas);
        _elementStartPosition = GetElementPosition(element);
        AnnotationCanvas.EditingMode = InkCanvasEditingMode.None;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void AnnotationElement_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_movingElement is null || !_movingElement.IsMouseCaptured)
        {
            return;
        }

        var current = e.GetPosition(AnnotationCanvas);
        var delta = current - _elementDragStart;
        SetElementPosition(_movingElement, new WpfPoint(_elementStartPosition.X + delta.X, _elementStartPosition.Y + delta.Y));
        UpdateSelectionFrame(_movingElement);
        e.Handled = true;
    }

    private void AnnotationElement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingElement is null)
        {
            return;
        }

        var movedElement = _movingElement;
        movedElement.ReleaseMouseCapture();
        _movingElement = null;

        var finalPosition = GetElementPosition(movedElement);
        if (Math.Abs(finalPosition.X - _elementStartPosition.X) > 0.5 || Math.Abs(finalPosition.Y - _elementStartPosition.Y) > 0.5)
        {
            _undoStack.Push(AnnotationAction.MoveElement(movedElement, _elementStartPosition, finalPosition));
        }

        SetActiveTool(_activeTool);
        e.Handled = true;
    }

    private bool TryNudgeSelectedElement(Key key)
    {
        if (_selectedElement is null || Keyboard.FocusedElement is System.Windows.Controls.TextBox)
        {
            return false;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default
        };

        if (delta == default)
        {
            return false;
        }

        var from = GetElementPosition(_selectedElement);
        SetElementPosition(_selectedElement, new WpfPoint(from.X + delta.X, from.Y + delta.Y));
        var to = GetElementPosition(_selectedElement);
        UpdateSelectionFrame(_selectedElement);

        if (Math.Abs(to.X - from.X) > 0.5 || Math.Abs(to.Y - from.Y) > 0.5)
        {
            _undoStack.Push(AnnotationAction.MoveElement(_selectedElement, from, to));
        }

        return true;
    }

    private void DeleteSelectedElement()
    {
        if (_selectedElement is null)
        {
            return;
        }

        AnnotationCanvas.Children.Remove(_selectedElement);
        ClearSelectedElement();
    }

    private void SelectElement(UIElement element)
    {
        _selectedElement = element;
        UpdateSelectionFrame(element);
    }

    private void ClearSelectedElement()
    {
        _selectedElement = null;
        if (ElementSelectionFrame is not null)
        {
            ElementSelectionFrame.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSelectionFrame(UIElement element)
    {
        CaptureSurface.UpdateLayout();
        var position = GetElementPosition(element);
        var size = GetElementSize(element);
        Canvas.SetLeft(ElementSelectionFrame, Math.Max(0, position.X - 4));
        Canvas.SetTop(ElementSelectionFrame, Math.Max(0, position.Y - 4));
        ElementSelectionFrame.Width = Math.Min(AnnotationCanvas.ActualWidth, size.Width + 8);
        ElementSelectionFrame.Height = Math.Min(AnnotationCanvas.ActualHeight, size.Height + 8);
        ElementSelectionFrame.Visibility = Visibility.Visible;
    }

    private WpfPoint GetElementPosition(UIElement element)
    {
        var left = InkCanvas.GetLeft(element);
        var top = InkCanvas.GetTop(element);
        return new WpfPoint(double.IsNaN(left) ? 0 : left, double.IsNaN(top) ? 0 : top);
    }

    private System.Windows.Size GetElementSize(UIElement element)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return new System.Windows.Size(0, 0);
        }

        frameworkElement.UpdateLayout();
        var width = frameworkElement.ActualWidth > 0 ? frameworkElement.ActualWidth : frameworkElement.Width;
        var height = frameworkElement.ActualHeight > 0 ? frameworkElement.ActualHeight : frameworkElement.Height;
        if (double.IsNaN(width) || width <= 0)
        {
            width = frameworkElement.MinWidth;
        }

        if (double.IsNaN(height) || height <= 0)
        {
            height = frameworkElement.MinHeight;
        }

        return new System.Windows.Size(width, height);
    }

    private void SetElementPosition(UIElement element, WpfPoint position)
    {
        var size = GetElementSize(element);
        var left = Math.Clamp(position.X, 0, Math.Max(0, AnnotationCanvas.ActualWidth - size.Width));
        var top = Math.Clamp(position.Y, 0, Math.Max(0, AnnotationCanvas.ActualHeight - size.Height));
        InkCanvas.SetLeft(element, left);
        InkCanvas.SetTop(element, top);
    }

    private void UpdateWatermarkPreview()
    {
        if (WatermarkBadge is null || WatermarkPreviewText is null || WatermarkTextBox is null || WatermarkCheckBox is null)
        {
            return;
        }

        var text = WatermarkTextBox.Text.Trim();
        WatermarkPreviewText.Text = text;
        WatermarkBadge.Visibility = WatermarkCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private BitmapSource RenderAnnotatedImage()
    {
        CaptureSurface.UpdateLayout();
        var pixelWidth = Math.Max(1, _sourceImage.PixelWidth);
        var pixelHeight = Math.Max(1, _sourceImage.PixelHeight);
        var dpi = _sourceImage.DpiX > 0 ? _sourceImage.DpiX : 96.0;
        var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        renderTarget.Render(CaptureSurface);
        renderTarget.Freeze();
        return renderTarget;
    }

    private static void SetToolButtonState(WpfButton button, bool isActive)
    {
        button.Background = new SolidColorBrush(isActive ? WpfColor.FromRgb(219, 234, 254) : WpfColors.White);
        button.BorderBrush = new SolidColorBrush(isActive ? WpfColor.FromRgb(37, 99, 235) : WpfColor.FromRgb(203, 213, 225));
        button.Foreground = new SolidColorBrush(isActive ? WpfColor.FromRgb(29, 78, 216) : WpfColor.FromRgb(17, 24, 39));
    }

    private static void SetColorButtonState(WpfButton button, bool isActive)
    {
        button.BorderBrush = new SolidColorBrush(isActive ? WpfColor.FromRgb(17, 24, 39) : WpfColor.FromRgb(203, 213, 225));
        button.BorderThickness = new Thickness(isActive ? 2 : 1);
    }

    private sealed record AnnotationAction(AnnotationActionKind Kind, Stroke? Stroke, UIElement? Element, WpfPoint? From, WpfPoint? To)
    {
        public static AnnotationAction AddStroke(Stroke stroke) => new(AnnotationActionKind.AddStroke, stroke, null, null, null);
        public static AnnotationAction AddElement(UIElement element) => new(AnnotationActionKind.AddElement, null, element, null, null);
        public static AnnotationAction MoveElement(UIElement element, WpfPoint from, WpfPoint to) => new(AnnotationActionKind.MoveElement, null, element, from, to);
    }
}
