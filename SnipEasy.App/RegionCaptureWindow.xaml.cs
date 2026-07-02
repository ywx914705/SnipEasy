using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SnipEasy.App.Services;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize = System.Windows.Size;

namespace SnipEasy.App;

public partial class RegionCaptureWindow : Window
{
    public enum CaptureAction
    {
        None,
        CopyToClipboard,
        Save,
        Sticker
    }


    private enum AnnotationTool
    {
        Select,
        Pen,
        Rectangle,
        Ellipse,
        Text,
        Arrow,
        Mosaic,
        Highlight,
        Blur
    }

    private enum DragMode
    {
        None,
        DrawingSelection,
        MovingSelection
    }

    private readonly BitmapSource _desktopSnapshot;
    private readonly Stack<AnnotationAction> _undoStack = new();
    private readonly bool _initialWatermarkEnabled;
    private readonly string _initialWatermarkText;
    private Rect _selection;
    private WpfPoint _dragStart;
    private WpfPoint _moveStart;
    private Rect _moveStartSelection;
    private DragMode _dragMode;
    private AnnotationTool _activeTool = AnnotationTool.Select;
    private WpfColor _strokeColor = WpfColor.FromRgb(239, 68, 68);
    private double _strokeThickness = 4;
    private WpfPoint _shapeStart;
    private Shape? _draftShape;
    private UIElement? _selectedElement;
    private UIElement? _movingElement;
    private UIElement? _moveCaptureElement;
    private WpfPoint _elementDragStart;
    private Rect _elementStartBounds;
    private FrameworkElement? _resizingElement;
    private Rect _resizeStartBounds;
    private Rect _resizeCurrentBounds;
    private string _activeResizeHandle = "";
    private Rect _selectionResizeCurrentBounds;
    private string _activeSelectionResizeHandle = "";

    private bool _mosaicPainting;
    private WriteableBitmap? _mosaicBitmap;
    private byte[]? _mosaicPixels;
    private byte[]? _mosaicSrcPixels;
    private System.Windows.Controls.Image? _mosaicImage;
    private double _mosaicScaleX;
    private double _mosaicScaleY;
    private int _mosaicBmpW;
    private int _mosaicBmpH;
    private int _mosaicSrcStride;
    private int _mosaicDstStride;

    // Blur painting state
    private bool _blurPainting;
    private WriteableBitmap? _blurBitmap;
    private byte[]? _blurPixels;
    private byte[]? _blurSrcPixels;
    private System.Windows.Controls.Image? _blurImage;
    private double _blurScaleX;
    private double _blurScaleY;
    private int _blurBmpW;
    private int _blurBmpH;
    private int _blurSrcStride;
    private int _blurDstStride;

    public BitmapSource? AnnotatedImage { get; private set; }
    public CaptureAction RequestedAction { get; private set; }
    public bool IsWatermarkApplied => false;

    public RegionCaptureWindow(BitmapSource desktopSnapshot, bool watermarkEnabled, string watermarkText)
    {
        _desktopSnapshot = desktopSnapshot;
        _initialWatermarkEnabled = watermarkEnabled;
        _initialWatermarkText = watermarkText;
        InitializeComponent();

        DesktopImage.Source = _desktopSnapshot;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // Watermark controls removed in simplified layout
        SetStrokeColor(_strokeColor);
        SetActiveTool(AnnotationTool.Select);
        UpdateDrawingAttributes();
        UpdateWatermarkPreview();

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            Keyboard.Focus(this);
            UpdateShades(Rect.Empty);
            PositionFloatingPanels();
        };
    }

    private void OverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateShades(_selection);
        PositionFloatingPanels();
        PositionSelectionResizeHandles();
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(OverlayCanvas);

        if (CaptureEditor.Visibility == Visibility.Visible && _activeTool == AnnotationTool.Select && _selection.Contains(position))
        {
            _dragMode = DragMode.MovingSelection;
            _moveStart = position;
            _moveStartSelection = _selection;
            OverlayCanvas.Cursor = WpfCursors.SizeAll;
            OverlayCanvas.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (CaptureEditor.Visibility == Visibility.Visible)
        {
            return;
        }

        _dragMode = DragMode.DrawingSelection;
        _dragStart = position;
        OverlayCanvas.CaptureMouse();
        SetSelection(new Rect(_dragStart, _dragStart), refreshImage: false);
        e.Handled = true;
    }

    private void OverlayCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var position = e.GetPosition(OverlayCanvas);

        if (_dragMode == DragMode.DrawingSelection)
        {
            UpdateSelection(_dragStart, position, refreshImage: false);
            e.Handled = true;
            return;
        }

        if (_dragMode == DragMode.MovingSelection)
        {
            MoveSelectionBy(position - _moveStart);
            e.Handled = true;
            return;
        }

        OverlayCanvas.Cursor = CaptureEditor.Visibility == Visibility.Visible && _activeTool == AnnotationTool.Select && _selection.Contains(position)
            ? WpfCursors.SizeAll
            : WpfCursors.Cross;
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragMode == DragMode.None)
        {
            return;
        }

        var finishedMode = _dragMode;
        _dragMode = DragMode.None;
        OverlayCanvas.ReleaseMouseCapture();
        OverlayCanvas.Cursor = WpfCursors.Cross;

        if (finishedMode == DragMode.DrawingSelection)
        {
            UpdateSelection(_dragStart, e.GetPosition(OverlayCanvas), refreshImage: true);
        }
        else if (finishedMode == DragMode.MovingSelection)
        {
            MoveSelectionBy(e.GetPosition(OverlayCanvas) - _moveStart);
        }

        if (!HasValidSelection())
        {
            ResetSelection();
            return;
        }

        ShowEditor();
        e.Handled = true;
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

        if ((e.Key is Key.Enter or Key.Return) && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && HasValidSelection())
        {
            Complete(CaptureAction.CopyToClipboard);
            e.Handled = true;
            return;
        }

        if (TryNudgeSelectedElement(e.Key) || TryNudgeSelection(e.Key))
        {
            e.Handled = true;
        }
    }

    private void SelectTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Select);
    private void PenTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Pen);
    private void RectangleTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Rectangle);
    private void EllipseTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Ellipse);
    private void TextTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Text);
    private void ArrowTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Arrow);
    private void MosaicTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Mosaic);
    private void HighlightTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Highlight);
    private void BlurTool_Click(object sender, RoutedEventArgs e) => SetActiveTool(AnnotationTool.Blur);
    private void RedColor_Click(object sender, RoutedEventArgs e) => SetStrokeColor(WpfColor.FromRgb(239, 68, 68));
    private void BlueColor_Click(object sender, RoutedEventArgs e) => SetStrokeColor(WpfColor.FromRgb(37, 99, 235));
    private void AmberColor_Click(object sender, RoutedEventArgs e) => SetStrokeColor(WpfColor.FromRgb(245, 158, 11));
    private void GreenColor_Click(object sender, RoutedEventArgs e) => SetStrokeColor(WpfColor.FromRgb(16, 185, 129));
    private void BlackColor_Click(object sender, RoutedEventArgs e) => SetStrokeColor(WpfColor.FromRgb(17, 24, 39));
    private void Copy_Click(object sender, RoutedEventArgs e) => Complete(CaptureAction.CopyToClipboard);
    private void Sticker_Click(object sender, RoutedEventArgs e) => Complete(CaptureAction.Sticker);
    private void Save_Click(object sender, RoutedEventArgs e) => Complete(CaptureAction.Save);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedElement is not null)
        {
            DeleteSelectedElement();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoLastAction();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
        ClearSelectedElement();
        _undoStack.Clear();
    }

    private void StrokeThicknessSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeThickness = e.NewValue;
        UpdateDrawingAttributes();
    }

    private void Watermark_Changed(object sender, RoutedEventArgs e) { }
    private void WatermarkTextBox_TextChanged(object sender, TextChangedEventArgs e) { }

    private void AnnotationCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        ClearSelectedElement();
        _undoStack.Push(AnnotationAction.AddStroke(e.Stroke));
    }

    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool == AnnotationTool.Pen)
        {
            ClearSelectedElement();
            return;
        }

        if (_activeTool == AnnotationTool.Select)
        {
            ClearSelectedElement();
            return;
        }

        var position = e.GetPosition(AnnotationCanvas);
        if (_activeTool == AnnotationTool.Text)
        {
            AddTextAnnotation(position);
            e.Handled = true;
            return;
        }

        if (_activeTool == AnnotationTool.Mosaic)
        {
            BeginMosaicPaint(position);
            e.Handled = true;
            return;
        }

        if (_activeTool == AnnotationTool.Highlight)
        {
            BeginHighlightPaint(position);
            e.Handled = true;
            return;
        }

        if (_activeTool == AnnotationTool.Blur)
        {
            BeginBlurPaint(position);
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
        if (_mosaicPainting && AnnotationCanvas.IsMouseCaptured)
        {
            ApplyMosaicBrush(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
            return;
        }

        if (_draftShape is null || !AnnotationCanvas.IsMouseCaptured)
        {
            return;
        }

        UpdateDraftShape(e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mosaicPainting)
        {
            EndMosaicPaint();
            e.Handled = true;
            return;
        }

        if (_draftShape is null || _activeTool is AnnotationTool.Pen or AnnotationTool.Select or AnnotationTool.Text)
        {
            return;
        }

        if (_activeTool == AnnotationTool.Arrow)
        {
            var arrowPath = (System.Windows.Shapes.Path)_draftShape;
            UpdateArrowShape(arrowPath, e.GetPosition(AnnotationCanvas));
            AnnotationCanvas.ReleaseMouseCapture();
            if (arrowPath.Data is not null)
            {
                AttachMovableElement(_draftShape);
                SelectElement(_draftShape);
                _undoStack.Push(AnnotationAction.AddElement(_draftShape));
            }
            else
            {
                AnnotationCanvas.Children.Remove(_draftShape);
            }
            _draftShape = null;
            e.Handled = true;
            return;
        }

        if (_activeTool == AnnotationTool.Highlight || _activeTool == AnnotationTool.Blur)
        {
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
            if (_activeTool == AnnotationTool.Mosaic)
            {
                ApplyMosaicFill(_draftShape);
            }

            AttachMovableElement(_draftShape);
            SelectElement(_draftShape);
            _undoStack.Push(AnnotationAction.AddElement(_draftShape));
        }

        _draftShape = null;
        e.Handled = true;
    }

    private void Complete(CaptureAction action)
    {
        if (!HasValidSelection())
        {
            return;
        }

        ClearSelectedElement();
        AnnotatedImage = RenderAnnotatedImage();
        RequestedAction = action;
        DialogResult = true;
        Close();
    }

    private void ShowEditor()
    {
        CaptureEditor.Visibility = Visibility.Visible;
        SelectionOutline.Visibility = Visibility.Visible;
        ToolBar.Visibility = Visibility.Visible;
        ActionBar.Visibility = Visibility.Visible;
        HintBar.Visibility = Visibility.Collapsed;
        RefreshSelectedImage();
        PositionSelectionOutline();
        PositionSelectionResizeHandles();
        PositionFloatingPanels();
        SetActiveTool(_activeTool);
    }

    private void ResetSelection()
    {
        _selection = Rect.Empty;
        CaptureEditor.Visibility = Visibility.Collapsed;
        SelectionOutline.Visibility = Visibility.Collapsed;
        ToolBar.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        HintBar.Visibility = Visibility.Visible;
        SetSelectionResizeHandlesVisibility(Visibility.Collapsed);
        ClearSelectedElement();
        AnnotationCanvas.Strokes.Clear();
        AnnotationCanvas.Children.Clear();
        UpdateShades(Rect.Empty);
        PositionFloatingPanels();
    }

    private void UpdateSelection(WpfPoint from, WpfPoint to, bool refreshImage)
    {
        var left = Math.Min(from.X, to.X);
        var top = Math.Min(from.Y, to.Y);
        var right = Math.Max(from.X, to.X);
        var bottom = Math.Max(from.Y, to.Y);
        SetSelection(new Rect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top)), refreshImage);
    }

    private void MoveSelectionBy(Vector delta)
    {
        SetSelection(new Rect(_moveStartSelection.Left + delta.X, _moveStartSelection.Top + delta.Y, _moveStartSelection.Width, _moveStartSelection.Height), refreshImage: true);
    }

    private bool TryNudgeSelection(Key key)
    {
        if (!HasValidSelection() || _activeTool != AnnotationTool.Select || _selectedElement is not null)
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

        SetSelection(new Rect(_selection.Left + delta.X, _selection.Top + delta.Y, _selection.Width, _selection.Height), refreshImage: true);
        return true;
    }

    private void SetSelection(Rect selection, bool refreshImage)
    {
        _selection = ClampSelection(selection);
        Canvas.SetLeft(CaptureEditor, _selection.Left);
        Canvas.SetTop(CaptureEditor, _selection.Top);
        CaptureEditor.Width = _selection.Width;
        CaptureEditor.Height = _selection.Height;
        CaptureSurface.Width = _selection.Width;
        CaptureSurface.Height = _selection.Height;
        SelectedImage.Width = _selection.Width;
        SelectedImage.Height = _selection.Height;
        AnnotationCanvas.Width = _selection.Width;
        AnnotationCanvas.Height = _selection.Height;
        SelectionOverlay.Width = _selection.Width;
        SelectionOverlay.Height = _selection.Height;
        UpdateShades(_selection);
        PositionSelectionOutline();
        PositionSelectionResizeHandles();
        PositionFloatingPanels();

        if (refreshImage && HasValidSelection())
        {
            RefreshSelectedImage();
        }
    }

    private Rect ClampSelection(Rect selection)
    {
        return CaptureSelectionGeometry.Clamp(selection, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);
    }

    private bool HasValidSelection()
    {
        return CaptureSelectionGeometry.IsValid(_selection);
    }

    private void RefreshSelectedImage()
    {
        var region = ToPixelRegion(_selection);
        var cropped = new CroppedBitmap(_desktopSnapshot, region);
        cropped.Freeze();
        SelectedImage.Source = cropped;
    }

    private Int32Rect ToPixelRegion(Rect region)
    {
        return CaptureSelectionGeometry.ToPixelRegion(region, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight, _desktopSnapshot.PixelWidth, _desktopSnapshot.PixelHeight);
    }

    private void UpdateShades(Rect selection)
    {
        var width = Math.Max(0, OverlayCanvas.ActualWidth);
        var height = Math.Max(0, OverlayCanvas.ActualHeight);

        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (selection.IsEmpty || selection.Width <= 0 || selection.Height <= 0)
        {
            SetRectangle(TopShade, 0, 0, width, height);
            SetRectangle(LeftShade, 0, 0, 0, 0);
            SetRectangle(RightShade, 0, 0, 0, 0);
            SetRectangle(BottomShade, 0, 0, 0, 0);
            return;
        }

        SetRectangle(TopShade, 0, 0, width, selection.Top);
        SetRectangle(LeftShade, 0, selection.Top, selection.Left, selection.Height);
        SetRectangle(RightShade, selection.Right, selection.Top, width - selection.Right, selection.Height);
        SetRectangle(BottomShade, 0, selection.Bottom, width, height - selection.Bottom);
    }

    private void PositionFloatingPanels()
    {
        if (OverlayCanvas.ActualWidth <= 0)
        {
            return;
        }

        // 提示栏居中显示
        HintBar.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(HintBar, Math.Max(18, (OverlayCanvas.ActualWidth - HintBar.DesiredSize.Width) / 2));
        Canvas.SetTop(HintBar, 18);

        if (!HasValidSelection())
        {
            ToolBar.Visibility = Visibility.Collapsed;
            ActionBar.Visibility = Visibility.Collapsed;
            return;
        }

        ToolBar.Visibility = Visibility.Visible;
        ActionBar.Visibility = Visibility.Visible;

        // 根据选区宽度决定紧凑程度（Snipaste 风格：不换行，只精简）
        var selW = _selection.Width;
        bool isCompact = selW < 520;
        bool isMini = selW < 240;
        SetToolButtonsCompact(isCompact, isMini);

        // 测量工具栏（不限制宽度，让它自然展开）
        ToolBar.ClearValue(FrameworkElement.MaxWidthProperty);
        ToolBar.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
        ActionBar.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));

        var toolWidth = ToolBar.DesiredSize.Width;
        var toolHeight = ToolBar.DesiredSize.Height;
        var actionWidth = ActionBar.DesiredSize.Width;
        var actionHeight = ActionBar.DesiredSize.Height;

        var screenW = OverlayCanvas.ActualWidth;
        var screenH = OverlayCanvas.ActualHeight;
        const double gap = 6;

        // ===== 工具栏：选区下方居中，允许超出选区边界 =====
        var toolLeft = _selection.Left + (_selection.Width - toolWidth) / 2;
        var toolTop = _selection.Bottom + gap;
        // 只限制不超出屏幕
        toolLeft = Math.Clamp(toolLeft, 4, screenW - toolWidth - 4);

        var toolInside = false;
        if (toolTop + toolHeight > screenH - 4)
        {
            toolTop = _selection.Top - toolHeight - gap;
        }
        if (toolTop < 4)
        {
            toolTop = _selection.Bottom - toolHeight - 4;
            toolInside = true;
        }
        if (toolTop < 4)
        {
            toolTop = 4;
        }

        // ===== 操作栏：选区右下角 =====
        double actionLeft, actionTop;
        if (toolInside)
        {
            actionLeft = _selection.Right - actionWidth - 4;
            actionTop = toolTop - actionHeight - gap;
        }
        else
        {
            actionLeft = _selection.Right - actionWidth - 4;
            actionTop = _selection.Bottom - actionHeight - 4;
        }
        actionLeft = Math.Clamp(actionLeft, 4, screenW - actionWidth - 4);
        actionTop = Math.Clamp(actionTop, 4, screenH - actionHeight - 4);

        // 防重叠
        if (IntersectsRect(actionLeft, actionTop, actionWidth, actionHeight,
                           toolLeft, toolTop, toolWidth, toolHeight))
        {
            actionTop = toolTop - actionHeight - gap;
            if (actionTop < 4)
            {
                actionTop = toolTop + toolHeight + gap;
            }
            actionTop = Math.Clamp(actionTop, 4, screenH - actionHeight - 4);
        }

        Canvas.SetLeft(ToolBar, toolLeft);
        Canvas.SetTop(ToolBar, toolTop);
        Canvas.SetLeft(ActionBar, actionLeft);
        Canvas.SetTop(ActionBar, actionTop);
    }

    private static bool IntersectsRect(double x1, double y1, double w1, double h1,
                                        double x2, double y2, double w2, double h2)
    {
        return x1 < x2 + w2 && x1 + w1 > x2 && y1 < y2 + h2 && y1 + h1 > y2;
    }

    private void SetToolButtonsCompact(bool compact, bool mini)
    {
        if (mini)
        {
            // 极简模式：首字，隐藏颜色/滑块/分隔线
            SetToolButtonText(SelectToolButton, SelectToolText, "选");
            SetToolButtonText(PenToolButton, PenToolText, "笔");
            SetToolButtonText(RectangleToolButton, RectangleToolText, "矩");
            SetToolButtonText(EllipseToolButton, EllipseToolText, "椭");
            SetToolButtonText(TextToolButton, TextToolText, "文");
            SetToolButtonText(ArrowToolButton, ArrowToolText, "箭");
            SetToolButtonText(MosaicToolButton, MosaicToolText, "马");

            foreach (var btn in new[] { SelectToolButton, PenToolButton, RectangleToolButton,
                                        EllipseToolButton, TextToolButton, ArrowToolButton, MosaicToolButton })
            {
                btn.MinWidth = 26;
                btn.Margin = new Thickness(1);
                btn.Padding = new Thickness(4, 5, 4, 5);
                btn.FontSize = 11;
            }

            SetVisible(RedColorButton, false);
            SetVisible(BlueColorButton, false);
            SetVisible(AmberColorButton, false);
            SetVisible(GreenColorButton, false);
            SetVisible(BlackColorButton, false);
            SetVisible(Sep1, false);
            SetVisible(Sep2, false);
            SetVisible(StrokeThicknessSlider, false);
        }
        else
        {
            // 恢复完整文字
            SetToolButtonText(SelectToolButton, SelectToolText, "选择");
            SetToolButtonText(PenToolButton, PenToolText, "画笔");
            SetToolButtonText(RectangleToolButton, RectangleToolText, "矩形");
            SetToolButtonText(EllipseToolButton, EllipseToolText, "椭圆");
            SetToolButtonText(TextToolButton, TextToolText, "文字");
            SetToolButtonText(ArrowToolButton, ArrowToolText, "箭头");
            SetToolButtonText(MosaicToolButton, MosaicToolText, "马赛克");

            double minW = compact ? 42 : 70;
            var pad = compact ? new Thickness(8, 6, 8, 6) : new Thickness(10, 6, 10, 6);
            foreach (var btn in new[] { SelectToolButton, PenToolButton, RectangleToolButton,
                                        EllipseToolButton, TextToolButton, ArrowToolButton, MosaicToolButton })
            {
                btn.MinWidth = minW;
                btn.Margin = new Thickness(3);
                btn.Padding = pad;
                btn.FontSize = 12;
            }

            SetVisible(RedColorButton, true);
            SetVisible(BlueColorButton, true);
            SetVisible(AmberColorButton, true);
            SetVisible(GreenColorButton, true);
            SetVisible(BlackColorButton, true);
            SetVisible(Sep1, true);
            SetVisible(Sep2, true);
            SetVisible(StrokeThicknessSlider, true);
        }
    }

    private static void SetToolButtonText(WpfButton button, TextBlock textBlock, string text)
    {
        textBlock.Text = text;
    }

    private static void SetVisible(UIElement element, bool visible)
    {
        element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
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
        SetToolButtonState(ArrowToolButton, tool == AnnotationTool.Arrow);
        SetToolButtonState(MosaicToolButton, tool == AnnotationTool.Mosaic);
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
        Shape shape = _activeTool switch
        {
            AnnotationTool.Rectangle => new WpfRectangle(),
            AnnotationTool.Arrow => CreateArrowPath(),
            AnnotationTool.Mosaic => new WpfRectangle(),
            _ => new Ellipse()
        };

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

    private void BeginHighlightPaint(WpfPoint canvasPos)
    {
        ClearSelectedElement();

        // Create a semi-transparent rectangle for highlighting
        var highlight = new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(WpfColor.FromArgb(80, 255, 255, 0)), // Yellow highlight
            Stroke = WpfBrushes.Transparent,
            StrokeThickness = 0
        };

        AnnotationCanvas.Children.Add(highlight);
        _draftShape = highlight;
        _shapeStart = canvasPos;
        AnnotationCanvas.CaptureMouse();
    }

    private void BeginBlurPaint(WpfPoint canvasPos)
    {
        ClearSelectedElement();

        // For now, use a simple blur effect placeholder
        // In a real implementation, this would apply Gaussian blur to the underlying image
        var blur = new System.Windows.Shapes.Rectangle
        {
            Fill = new SolidColorBrush(WpfColor.FromArgb(128, 200, 200, 200)),
            Stroke = new SolidColorBrush(WpfColor.FromArgb(60, 128, 128, 128)),
            StrokeThickness = 1
        };

        AnnotationCanvas.Children.Add(blur);
        _draftShape = blur;
        _shapeStart = canvasPos;
        AnnotationCanvas.CaptureMouse();
    }

    private void UpdateDraftShape(WpfPoint current)
    {
        if (_draftShape is null)
        {
            return;
        }

        if (_draftShape is System.Windows.Shapes.Path path && _activeTool == AnnotationTool.Arrow)
        {
            UpdateArrowShape(path, current);
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

    private System.Windows.Shapes.Path CreateArrowPath()
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new WpfPoint(0, 0), false, false);
            ctx.LineTo(new WpfPoint(1, 0), true, false);
        }
        geometry.Freeze();

        var path = new System.Windows.Shapes.Path
        {
            Stroke = new SolidColorBrush(_strokeColor),
            StrokeThickness = _strokeThickness,
            Fill = null,
            Data = geometry,
            SnapsToDevicePixels = true,
            Tag = "Arrow"
        };
        return path;
    }

    private void UpdateArrowShape(System.Windows.Shapes.Path path, WpfPoint current)
    {
        var start = _shapeStart;
        var end = current;

        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 5) return;

        var angle = Math.Atan2(dy, dx);
        var headLen = Math.Min(22, length * 0.3);
        var headW = headLen * 0.7;

        var tip = end;
        var left = new WpfPoint(
            end.X - headLen * Math.Cos(angle) + headW * Math.Sin(angle),
            end.Y - headLen * Math.Sin(angle) - headW * Math.Cos(angle));
        var right = new WpfPoint(
            end.X - headLen * Math.Cos(angle) - headW * Math.Sin(angle),
            end.Y - headLen * Math.Sin(angle) + headW * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.LineTo(tip, true, false);
            ctx.BeginFigure(tip, true, true);
            ctx.LineTo(left, true, false);
            ctx.LineTo(right, true, false);
        }

        geometry.Freeze();
        path.Data = geometry;
        path.Fill = new SolidColorBrush(_strokeColor);
        InkCanvas.SetLeft(path, 0);
        InkCanvas.SetTop(path, 0);
    }

    private void ApplyMosaicFill(Shape shape)
    {
        const int blockSize = 14;

        var shapeLeft = InkCanvas.GetLeft(shape);
        var shapeTop = InkCanvas.GetTop(shape);
        var w = (int)Math.Max(1, shape.Width);
        var h = (int)Math.Max(1, shape.Height);

        var snapW = _desktopSnapshot.PixelWidth;
        var snapH = _desktopSnapshot.PixelHeight;
        var overlayW = Math.Max(1, OverlayCanvas.ActualWidth);
        var overlayH = Math.Max(1, OverlayCanvas.ActualHeight);
        var sx = snapW / overlayW;
        var sy = snapH / overlayH;

        var srcStride = snapW * 4;
        var srcPixels = new byte[srcStride * snapH];
        _desktopSnapshot.CopyPixels(srcPixels, srcStride, 0);

        var dstBmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
        var dstStride = w * 4;
        var dstPixels = new byte[dstStride * h];

        for (var bx = 0; bx < w; bx += blockSize)
        {
            for (var by = 0; by < h; by += blockSize)
            {
                var cx = (int)((shapeLeft + bx + blockSize / 2.0) * sx);
                var cy = (int)((shapeTop + by + blockSize / 2.0) * sy);
                cx = Math.Clamp(cx, 0, snapW - 1);
                cy = Math.Clamp(cy, 0, snapH - 1);

                long r = 0, g = 0, b = 0, cnt = 0;
                var half = blockSize / 2;
                for (var dx = -half; dx <= half; dx++)
                {
                    for (var dy = -half; dy <= half; dy++)
                    {
                        var px = Math.Clamp(cx + dx, 0, snapW - 1);
                        var py = Math.Clamp(cy + dy, 0, snapH - 1);
                        var off = (py * snapW + px) * 4;
                        b += srcPixels[off];
                        g += srcPixels[off + 1];
                        r += srcPixels[off + 2];
                        cnt++;
                    }
                }

                var avgR = (byte)(r / Math.Max(1, cnt));
                var avgG = (byte)(g / Math.Max(1, cnt));
                var avgB = (byte)(b / Math.Max(1, cnt));

                var bw = Math.Min(blockSize, w - bx);
                var bh = Math.Min(blockSize, h - by);
                for (var px2 = 0; px2 < bw; px2++)
                {
                    for (var py2 = 0; py2 < bh; py2++)
                    {
                        var off = ((by + py2) * dstStride + (bx + px2) * 4);
                        dstPixels[off] = avgB;
                        dstPixels[off + 1] = avgG;
                        dstPixels[off + 2] = avgR;
                        dstPixels[off + 3] = 255;
                    }
                }
            }
        }

        dstBmp.Lock();
        dstBmp.WritePixels(new Int32Rect(0, 0, w, h), dstPixels, dstStride, 0);
        dstBmp.Unlock();
        dstBmp.Freeze();

        var brush = new ImageBrush(dstBmp)
        {
            Stretch = Stretch.Fill,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        shape.Fill = brush;
        shape.Stroke = new SolidColorBrush(WpfColor.FromArgb(60, 128, 128, 128));
        shape.StrokeThickness = 0.5;
    }

    private void BeginMosaicPaint(WpfPoint canvasPos)
    {
        ClearSelectedElement();

        var region = ToPixelRegion(_selection);
        _mosaicBmpW = region.Width;
        _mosaicBmpH = region.Height;
        _mosaicScaleX = region.Width / Math.Max(1, CaptureSurface.ActualWidth);
        _mosaicScaleY = region.Height / Math.Max(1, CaptureSurface.ActualHeight);

        _mosaicSrcStride = _desktopSnapshot.PixelWidth * 4;
        _mosaicSrcPixels = new byte[_mosaicSrcStride * _desktopSnapshot.PixelHeight];
        _desktopSnapshot.CopyPixels(_mosaicSrcPixels, _mosaicSrcStride, 0);

        _mosaicDstStride = _mosaicBmpW * 4;
        _mosaicPixels = new byte[_mosaicDstStride * _mosaicBmpH];

        _mosaicBitmap = new WriteableBitmap(_mosaicBmpW, _mosaicBmpH, 96, 96, PixelFormats.Pbgra32, null);
        _mosaicBitmap.WritePixels(new Int32Rect(0, 0, _mosaicBmpW, _mosaicBmpH), _mosaicPixels, _mosaicDstStride, 0);

        _mosaicImage = new System.Windows.Controls.Image
        {
            Source = _mosaicBitmap,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        };
        InkCanvas.SetLeft(_mosaicImage, 0);
        InkCanvas.SetTop(_mosaicImage, 0);
        _mosaicImage.Width = CaptureSurface.ActualWidth;
        _mosaicImage.Height = CaptureSurface.ActualHeight;
        AnnotationCanvas.Children.Add(_mosaicImage);

        _mosaicPainting = true;
        AnnotationCanvas.CaptureMouse();
        ApplyMosaicBrush(canvasPos);
    }

    private void ApplyMosaicBrush(WpfPoint canvasPos)
    {
        const int brushRadius = 18;
        const int block = 10;

        var cx = (int)(canvasPos.X * _mosaicScaleX);
        var cy = (int)(canvasPos.Y * _mosaicScaleY);

        var srcW = _desktopSnapshot.PixelWidth;
        var srcH = _desktopSnapshot.PixelHeight;

        var changed = false;

        for (var bx = cx - brushRadius; bx < cx + brushRadius; bx += block)
        {
            for (var by = cy - brushRadius; by < cy + brushRadius; by += block)
            {
                if (bx < 0 || by < 0 || bx >= _mosaicBmpW || by >= _mosaicBmpH) continue;

                long r = 0, g = 0, b = 0, cnt = 0;
                for (var dx = 0; dx < block; dx++)
                {
                    for (var dy = 0; dy < block; dy++)
                    {
                        var srcX = Math.Clamp(bx + dx, 0, srcW - 1);
                        var srcY = Math.Clamp(by + dy, 0, srcH - 1);
                        var off = (srcY * srcW + srcX) * 4;
                        b += _mosaicSrcPixels[off];
                        g += _mosaicSrcPixels[off + 1];
                        r += _mosaicSrcPixels[off + 2];
                        cnt++;
                    }
                }
                if (cnt == 0) continue;

                var avgR = (byte)(r / cnt);
                var avgG = (byte)(g / cnt);
                var avgB = (byte)(b / cnt);

                var bw = Math.Min(block, _mosaicBmpW - bx);
                var bh = Math.Min(block, _mosaicBmpH - by);
                for (var px = 0; px < bw; px++)
                {
                    for (var py = 0; py < bh; py++)
                    {
                        var off = ((by + py) * _mosaicDstStride + (bx + px) * 4);
                        if (off >= 0 && off + 3 < _mosaicPixels.Length)
                        {
                            _mosaicPixels[off] = avgB;
                            _mosaicPixels[off + 1] = avgG;
                            _mosaicPixels[off + 2] = avgR;
                            _mosaicPixels[off + 3] = 255;
                        }
                    }
                }
                changed = true;
            }
        }

        if (changed && _mosaicBitmap != null)
        {
            _mosaicBitmap.Lock();
            _mosaicBitmap.WritePixels(new Int32Rect(0, 0, _mosaicBmpW, _mosaicBmpH), _mosaicPixels, _mosaicDstStride, 0);
            _mosaicBitmap.Unlock();
        }
    }

    private void EndMosaicPaint()
    {
        AnnotationCanvas.ReleaseMouseCapture();
        _mosaicPainting = false;

        if (_mosaicBitmap != null)
        {
            _mosaicBitmap.Freeze();
        }

        if (_mosaicImage != null)
        {
            AttachMovableElement(_mosaicImage);
            SelectElement(_mosaicImage);
            _undoStack.Push(AnnotationAction.AddElement(_mosaicImage));
        }

        _mosaicBitmap = null;
        _mosaicPixels = null;
        _mosaicSrcPixels = null;
        _mosaicImage = null;
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

        if (action.Kind == AnnotationActionKind.ChangeElementBounds && action.Element is not null && action.FromBounds is not null)
        {
            SetElementBounds(action.Element, action.FromBounds.Value);
            if (_selectedElement == action.Element)
            {
                UpdateSelectionFrame(action.Element);
            }
        }
    }

    private void AttachMovableElement(UIElement element)
    {
        element.PreviewMouseLeftButtonDown += AnnotationElement_PreviewMouseLeftButtonDown;
        element.MouseLeftButtonDown += AnnotationElement_MouseLeftButtonDown;
        element.MouseMove += AnnotationElement_MouseMove;
        element.MouseLeftButtonUp += AnnotationElement_MouseLeftButtonUp;
    }

    private void AnnotationElement_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || _draftShape is not null)
        {
            return;
        }

        SelectElement(element);
    }

    private void AnnotationElement_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || _draftShape is not null)
        {
            return;
        }

        if (IsFromTextBox(e.OriginalSource))
        {
            SelectElement(element);
            return;
        }

        SelectElement(element);
        BeginElementMove(element, element, e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void AnnotationElement_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_movingElement is null || _moveCaptureElement != sender)
        {
            return;
        }

        MoveElementTo(e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void AnnotationElement_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingElement is null || _moveCaptureElement != sender)
        {
            return;
        }

        FinishElementMove();
        e.Handled = true;
    }

    private void SelectionFrame_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedElement is null)
        {
            return;
        }

        BeginElementMove(_selectedElement, ElementSelectionFrame, e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void SelectionFrame_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_movingElement is null || _moveCaptureElement != ElementSelectionFrame)
        {
            return;
        }

        MoveElementTo(e.GetPosition(AnnotationCanvas));
        e.Handled = true;
    }

    private void SelectionFrame_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingElement is null || _moveCaptureElement != ElementSelectionFrame)
        {
            return;
        }

        FinishElementMove();
        e.Handled = true;
    }

    private void BeginElementMove(UIElement element, UIElement captureTarget, WpfPoint startPoint)
    {
        _movingElement = element;
        _moveCaptureElement = captureTarget;
        _elementDragStart = startPoint;
        _elementStartBounds = GetElementBounds(element);
        AnnotationCanvas.EditingMode = InkCanvasEditingMode.None;
        captureTarget.CaptureMouse();
    }

    private void MoveElementTo(WpfPoint current)
    {
        if (_movingElement is null)
        {
            return;
        }

        var delta = current - _elementDragStart;
        var nextBounds = new Rect(
            _elementStartBounds.Left + delta.X,
            _elementStartBounds.Top + delta.Y,
            _elementStartBounds.Width,
            _elementStartBounds.Height);
        SetElementBounds(_movingElement, nextBounds);
        UpdateSelectionFrame(_movingElement);
    }

    private void FinishElementMove()
    {
        if (_movingElement is null)
        {
            return;
        }

        var movedElement = _movingElement;
        var captureTarget = _moveCaptureElement;
        captureTarget?.ReleaseMouseCapture();
        _movingElement = null;
        _moveCaptureElement = null;

        var finalBounds = GetElementBounds(movedElement);
        PushBoundsChangeIfNeeded(movedElement, _elementStartBounds, finalBounds);
        SetActiveTool(_activeTool);
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

        var from = GetElementBounds(_selectedElement);
        SetElementBounds(_selectedElement, new Rect(from.Left + delta.X, from.Top + delta.Y, from.Width, from.Height));
        var to = GetElementBounds(_selectedElement);
        UpdateSelectionFrame(_selectedElement);
        PushBoundsChangeIfNeeded(_selectedElement, from, to);

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

        SetResizeHandlesVisibility(Visibility.Collapsed);
    }

    private void UpdateSelectionFrame(UIElement element)
    {
        CaptureSurface.UpdateLayout();
        var bounds = GetElementBounds(element);
        var frameLeft = Math.Max(0, bounds.Left - 4);
        var frameTop = Math.Max(0, bounds.Top - 4);
        var frameWidth = Math.Min(Math.Max(0, AnnotationCanvas.ActualWidth - frameLeft), bounds.Width + 8);
        var frameHeight = Math.Min(Math.Max(0, AnnotationCanvas.ActualHeight - frameTop), bounds.Height + 8);

        Canvas.SetLeft(ElementSelectionFrame, frameLeft);
        Canvas.SetTop(ElementSelectionFrame, frameTop);
        ElementSelectionFrame.Width = frameWidth;
        ElementSelectionFrame.Height = frameHeight;
        ElementSelectionFrame.Visibility = Visibility.Visible;
        PositionResizeHandles(new Rect(frameLeft, frameTop, frameWidth, frameHeight));
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
        var bounds = GetElementBounds(element);
        SetElementBounds(element, new Rect(position.X, position.Y, bounds.Width, bounds.Height));
    }

    private Rect GetElementBounds(UIElement element)
    {
        var position = GetElementPosition(element);
        var size = GetElementSize(element);
        return new Rect(position.X, position.Y, size.Width, size.Height);
    }

    private void SetElementBounds(UIElement element, Rect bounds)
    {
        if (element is not FrameworkElement frameworkElement)
        {
            return;
        }

        var clamped = ClampElementBounds(element, bounds);
        frameworkElement.Width = clamped.Width;
        frameworkElement.Height = clamped.Height;
        InkCanvas.SetLeft(element, clamped.Left);
        InkCanvas.SetTop(element, clamped.Top);
    }

    private Rect ClampElementBounds(UIElement element, Rect bounds)
    {
        var minimum = GetMinimumElementSize(element);
        var canvasWidth = Math.Max(0, AnnotationCanvas.ActualWidth);
        var canvasHeight = Math.Max(0, AnnotationCanvas.ActualHeight);
        var maxWidth = Math.Max(minimum.Width, canvasWidth);
        var maxHeight = Math.Max(minimum.Height, canvasHeight);
        var width = Math.Clamp(bounds.Width, minimum.Width, maxWidth);
        var height = Math.Clamp(bounds.Height, minimum.Height, maxHeight);
        var left = Math.Clamp(bounds.Left, 0, Math.Max(0, canvasWidth - width));
        var top = Math.Clamp(bounds.Top, 0, Math.Max(0, canvasHeight - height));
        return new Rect(left, top, width, height);
    }

    private static WpfSize GetMinimumElementSize(UIElement element)
    {
        if (element is Border)
        {
            return new WpfSize(56, 30);
        }

        return new WpfSize(12, 12);
    }

    private void ResizeHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (_selectedElement is not FrameworkElement element || sender is not Thumb thumb)
        {
            return;
        }

        _resizingElement = element;
        _resizeStartBounds = GetElementBounds(element);
        _resizeCurrentBounds = _resizeStartBounds;
        _activeResizeHandle = thumb.Tag?.ToString() ?? "";
        e.Handled = true;
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_resizingElement is null)
        {
            return;
        }

        var resized = ResizeBounds(_resizeCurrentBounds, e.HorizontalChange, e.VerticalChange, _activeResizeHandle);
        SetElementBounds(_resizingElement, resized);
        _resizeCurrentBounds = GetElementBounds(_resizingElement);
        UpdateSelectionFrame(_resizingElement);
        e.Handled = true;
    }

    private void ResizeHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (_resizingElement is null)
        {
            return;
        }

        var resizedElement = _resizingElement;
        var finalBounds = GetElementBounds(resizedElement);
        PushBoundsChangeIfNeeded(resizedElement, _resizeStartBounds, finalBounds);
        _resizingElement = null;
        _activeResizeHandle = "";
        e.Handled = true;
    }

    private Rect ResizeBounds(Rect bounds, double horizontalChange, double verticalChange, string handle)
    {
        return CaptureSelectionGeometry.Resize(bounds, horizontalChange, verticalChange, handle, AnnotationCanvas.ActualWidth, AnnotationCanvas.ActualHeight);
    }

    private void PositionResizeHandles(Rect frame)
    {
        SetResizeHandlePosition(ResizeTopLeftHandle, frame.Left, frame.Top);
        SetResizeHandlePosition(ResizeTopRightHandle, frame.Right, frame.Top);
        SetResizeHandlePosition(ResizeBottomLeftHandle, frame.Left, frame.Bottom);
        SetResizeHandlePosition(ResizeBottomRightHandle, frame.Right, frame.Bottom);
        SetResizeHandlesVisibility(Visibility.Visible);
    }

    private static void SetResizeHandlePosition(Thumb handle, double centerX, double centerY)
    {
        Canvas.SetLeft(handle, Math.Max(0, centerX - handle.Width / 2));
        Canvas.SetTop(handle, Math.Max(0, centerY - handle.Height / 2));
    }

    private void SetResizeHandlesVisibility(Visibility visibility)
    {
        ResizeTopLeftHandle.Visibility = visibility;
        ResizeTopRightHandle.Visibility = visibility;
        ResizeBottomLeftHandle.Visibility = visibility;
        ResizeBottomRightHandle.Visibility = visibility;
    }

    private void SelectionResizeHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb thumb || !HasValidSelection())
        {
            return;
        }

        ClearSelectedElement();
        _selectionResizeCurrentBounds = _selection;
        _activeSelectionResizeHandle = thumb.Tag?.ToString() ?? "";
        OverlayCanvas.Cursor = thumb.Cursor;
        e.Handled = true;
    }

    private void SelectionResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_activeSelectionResizeHandle))
        {
            return;
        }

        var resized = ResizeSelectionBounds(
            _selectionResizeCurrentBounds,
            e.HorizontalChange,
            e.VerticalChange,
            _activeSelectionResizeHandle);
        SetSelection(resized, refreshImage: true);
        _selectionResizeCurrentBounds = _selection;
        e.Handled = true;
    }

    private void SelectionResizeHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _activeSelectionResizeHandle = "";
        OverlayCanvas.Cursor = WpfCursors.Cross;
        PositionSelectionResizeHandles();
        e.Handled = true;
    }

    private Rect ResizeSelectionBounds(Rect bounds, double horizontalChange, double verticalChange, string handle)
    {
        return CaptureSelectionGeometry.Resize(bounds, horizontalChange, verticalChange, handle, OverlayCanvas.ActualWidth, OverlayCanvas.ActualHeight);
    }

    private void PositionSelectionResizeHandles()
    {
        if (CaptureEditor is null || CaptureEditor.Visibility != Visibility.Visible || !HasValidSelection())
        {
            if (SelectionTopLeftHandle is not null)
            {
                SetSelectionResizeHandlesVisibility(Visibility.Collapsed);
            }

            return;
        }

        SetResizeHandlePosition(SelectionTopLeftHandle, _selection.Left, _selection.Top);
        SetResizeHandlePosition(SelectionTopRightHandle, _selection.Right, _selection.Top);
        SetResizeHandlePosition(SelectionBottomLeftHandle, _selection.Left, _selection.Bottom);
        SetResizeHandlePosition(SelectionBottomRightHandle, _selection.Right, _selection.Bottom);
        SetSelectionResizeHandlesVisibility(Visibility.Visible);
    }

    private void PositionSelectionOutline()
    {
        if (SelectionOutline is null)
        {
            return;
        }

        if (!HasValidSelection() || CaptureEditor.Visibility != Visibility.Visible)
        {
            SelectionOutline.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(SelectionOutline, _selection.Left);
        Canvas.SetTop(SelectionOutline, _selection.Top);
        SelectionOutline.Width = _selection.Width;
        SelectionOutline.Height = _selection.Height;
        SelectionOutline.Visibility = Visibility.Visible;
    }

    private void SetSelectionResizeHandlesVisibility(Visibility visibility)
    {
        SelectionTopLeftHandle.Visibility = visibility;
        SelectionTopRightHandle.Visibility = visibility;
        SelectionBottomLeftHandle.Visibility = visibility;
        SelectionBottomRightHandle.Visibility = visibility;
    }

    private void PushBoundsChangeIfNeeded(UIElement element, Rect from, Rect to)
    {
        if (Math.Abs(to.Left - from.Left) > 0.5 ||
            Math.Abs(to.Top - from.Top) > 0.5 ||
            Math.Abs(to.Width - from.Width) > 0.5 ||
            Math.Abs(to.Height - from.Height) > 0.5)
        {
            _undoStack.Push(AnnotationAction.ChangeElementBounds(element, from, to));
        }
    }

    private static bool IsFromTextBox(object originalSource)
    {
        if (originalSource is not DependencyObject current)
        {
            return false;
        }

        while (current is not null)
        {
            if (current is System.Windows.Controls.TextBox)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void UpdateWatermarkPreview() { }

    private BitmapSource RenderAnnotatedImage()
    {
        CaptureSurface.UpdateLayout();
        var region = ToPixelRegion(_selection);
        var scaleX = region.Width / Math.Max(1, CaptureSurface.ActualWidth);
        var scaleY = region.Height / Math.Max(1, CaptureSurface.ActualHeight);
        var renderTarget = new RenderTargetBitmap(region.Width, region.Height, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32);
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

    private static void SetRectangle(WpfRectangle rectangle, double left, double top, double width, double height)
    {
        Canvas.SetLeft(rectangle, left);
        Canvas.SetTop(rectangle, top);
        rectangle.Width = Math.Max(0, width);
        rectangle.Height = Math.Max(0, height);
    }
}
