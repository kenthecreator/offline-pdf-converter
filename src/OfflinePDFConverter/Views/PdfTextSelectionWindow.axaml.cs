using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Controls.Shapes;
using OfflinePDFConverter.Models;
using System.Runtime.InteropServices;

namespace OfflinePDFConverter.Views;

public partial class PdfTextSelectionWindow : Window
{
    private const double MinimumZoomPercent = 25;
    private const double MaximumZoomPercent = 400;
    private const double ZoomStepPercent = 25;
    private static readonly IBrush SelectedBrush = new SolidColorBrush(Color.FromArgb(104, 49, 133, 232));
    private static readonly IBrush SelectionHandleBrush = new SolidColorBrush(Color.FromRgb(35, 105, 215));
    private IReadOnlyList<PdfSelectableWord> _words = Array.Empty<PdfSelectableWord>();
    private readonly HashSet<int> _selectedIndices = new();
    private readonly Avalonia.Controls.Shapes.Path _selectionHighlightPath = new()
    {
        Fill = SelectedBrush,
        IsHitTestVisible = false
    };
    private readonly List<Control> _selectionHandleElements = new();
    private Canvas _pageCanvas = null!;
    private LayoutTransformControl _pageZoomTransform = null!;
    private ScrollViewer _previewScrollViewer = null!;
    private TextBlock _zoomValueText = null!;
    private TextBlock _selectionStatusText = null!;
    private Point _selectionStartPoint;
    private int? _anchorIndex;
    private int? _extensionAnchorIndex;
    private IReadOnlyCollection<int> _gesturePreviousSelection = Array.Empty<int>();
    private bool _gestureAdditive;
    private bool _gestureShiftHeld;
    private bool _gestureExtend;
    private bool _dragSelecting;
    private bool _didDrag;
    private double _zoomScale = 1;
    private double _zoomPercent = 100;
    private double? _pinchStartZoomPercent;

    public PdfTextSelectionWindow()
    {
        InitializeComponent();
    }

    public PdfTextSelectionWindow(
        string pageTitle,
        Bitmap pageImage,
        double pageWidth,
        double pageHeight,
        IReadOnlyList<PdfSelectableWord> words,
        IReadOnlyCollection<int> selectedIndices)
        : this()
    {
        _words = words;
        _selectedIndices.UnionWith(selectedIndices);
        if (_selectedIndices.Count > 0) _extensionAnchorIndex = _selectedIndices.Min();
        _pageCanvas = this.FindControl<Canvas>("PageCanvas")
            ?? throw new InvalidOperationException("PageCanvasが見つかりません。");
        _pageZoomTransform = this.FindControl<LayoutTransformControl>("PageZoomTransform")
            ?? throw new InvalidOperationException("PageZoomTransformが見つかりません。");
        _previewScrollViewer = this.FindControl<ScrollViewer>("PreviewScrollViewer")
            ?? throw new InvalidOperationException("PreviewScrollViewerが見つかりません。");
        _zoomValueText = this.FindControl<TextBlock>("ZoomValueText")
            ?? throw new InvalidOperationException("ZoomValueTextが見つかりません。");
        _selectionStatusText = this.FindControl<TextBlock>("SelectionStatusText")
            ?? throw new InvalidOperationException("SelectionStatusTextが見つかりません。");
        var titleText = this.FindControl<TextBlock>("TitleText")
            ?? throw new InvalidOperationException("TitleTextが見つかりません。");
        var copyShortcutText = this.FindControl<TextBlock>("CopyShortcutText")
            ?? throw new InvalidOperationException("CopyShortcutTextが見つかりません。");
        var zoomGestureHelpText = this.FindControl<TextBlock>("ZoomGestureHelpText")
            ?? throw new InvalidOperationException("ZoomGestureHelpTextが見つかりません。");

        titleText.Text = pageTitle;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            copyShortcutText.Text = "ドラッグで選択 · ⌘で追加 · Shiftで範囲を伸ばす · ⌘Cでコピー";
            zoomGestureHelpText.Text = "ピンチ操作、または⌘＋マウスホイールで拡大縮小";
        }
        else
        {
            copyShortcutText.Text = "ドラッグで選択 · Ctrlで追加 · Shiftで範囲を伸ばす · Ctrl+Cでコピー";
            zoomGestureHelpText.Text = "ピンチ操作、またはCtrl＋マウスホイールで拡大縮小";
        }
        _pageCanvas.Width = pageWidth;
        _pageCanvas.Height = pageHeight;
        _pageCanvas.Children.Add(new Image
        {
            Source = pageImage,
            Width = pageWidth,
            Height = pageHeight,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        });

        _pageCanvas.Children.Add(_selectionHighlightPath);
        _pageCanvas.Cursor = new Cursor(StandardCursorType.Ibeam);
        _pageCanvas.PointerPressed += OnPagePointerPressed;
        _pageCanvas.PointerMoved += OnPagePointerMoved;
        _pageCanvas.PointerReleased += OnPagePointerReleased;
        _pageCanvas.PointerCaptureLost += OnPagePointerCaptureLost;
        Gestures.AddPinchHandler(_previewScrollViewer, OnPreviewPinch);
        Gestures.AddPinchEndedHandler(_previewScrollViewer, OnPreviewPinchEnded);
        Gestures.AddPointerTouchPadGestureMagnifyHandler(_previewScrollViewer, OnPreviewTouchPadMagnify);

        ApplyZoom(100);
        UpdateSelectionVisuals();
        UpdateStatus();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var copyModifier = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(copyModifier))
        {
            await CopySelectedTextAsync();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(copyModifier))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.OemPlus:
            case Key.Add:
                ChangeZoom(ZoomStepPercent);
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                ChangeZoom(-ZoomStepPercent);
                e.Handled = true;
                break;
            case Key.D0:
                ApplyZoom(100);
                e.Handled = true;
                break;
        }
    }

    private void OnResetZoomClick(object? sender, RoutedEventArgs e)
    {
        ApplyZoom(100);
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var zoomModifier = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        if (!e.KeyModifiers.HasFlag(zoomModifier) || Math.Abs(e.Delta.Y) < double.Epsilon)
        {
            return;
        }

        ChangeZoom(Math.Sign(e.Delta.Y) * ZoomStepPercent);
        e.Handled = true;
    }

    private void OnPreviewPinch(object? sender, PinchEventArgs e)
    {
        _pinchStartZoomPercent ??= _zoomPercent;
        ApplyZoom(_pinchStartZoomPercent.Value * e.Scale);
        e.Handled = true;
    }

    private void OnPreviewPinchEnded(object? sender, PinchEndedEventArgs e)
    {
        _pinchStartZoomPercent = null;
        e.Handled = true;
    }

    private void OnPreviewTouchPadMagnify(object? sender, PointerDeltaEventArgs e)
    {
        var magnification = Math.Abs(e.Delta.Y) > double.Epsilon ? e.Delta.Y : e.Delta.X;
        if (Math.Abs(magnification) < double.Epsilon)
        {
            return;
        }

        ApplyZoom(_zoomPercent * Math.Exp(magnification));
        e.Handled = true;
    }

    private void ChangeZoom(double difference)
    {
        ApplyZoom(_zoomPercent + difference);
    }

    private void ApplyZoom(double zoomPercent)
    {
        var clamped = Math.Clamp(zoomPercent, MinimumZoomPercent, MaximumZoomPercent);
        var scale = clamped / 100d;
        _zoomPercent = clamped;
        _zoomScale = scale;
        _pageZoomTransform.LayoutTransform = new ScaleTransform(scale, scale);
        _zoomValueText.Text = $"{clamped:0}%";

        UpdateSelectionVisuals();
    }

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_pageCanvas).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _selectionStartPoint = ClampToPage(e.GetPosition(_pageCanvas));
        _anchorIndex = PdfTextSelectionLogic.HitCharacter(_words, _selectionStartPoint);
        _gestureAdditive = PdfTextSelectionLogic.IsAdditiveModifier(
            e.KeyModifiers, RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        _gestureShiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _gestureExtend = _gestureShiftHeld && _extensionAnchorIndex.HasValue;
        _gesturePreviousSelection = _selectedIndices.ToArray();
        _dragSelecting = true;
        _didDrag = false;
        e.Pointer.Capture(_pageCanvas);
        e.Handled = true;
    }

    private void OnPagePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragSelecting)
        {
            return;
        }

        var current = ClampToPage(e.GetPosition(_pageCanvas));
        if (PdfTextSelectionLogic.IsDrag(_selectionStartPoint, current, _zoomScale)) _didDrag = true;
        if (_didDrag && _anchorIndex.HasValue)
            ApplyGestureSelection(PdfTextSelectionLogic.FindCharacter(_words, current));
        e.Handled = true;
    }

    private void OnPagePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragSelecting)
        {
            return;
        }

        var end = ClampToPage(e.GetPosition(_pageCanvas));
        if (PdfTextSelectionLogic.IsDrag(_selectionStartPoint, end, _zoomScale)) _didDrag = true;
        if (!_didDrag && !_gestureAdditive && !_gestureShiftHeld)
        {
            ClearSelection();
        }
        else if (_didDrag && _anchorIndex.HasValue)
        {
            ApplyGestureSelection(PdfTextSelectionLogic.FindCharacter(_words, end));
            if (!_gestureExtend) _extensionAnchorIndex = _anchorIndex;
        }
        _dragSelecting = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPagePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragSelecting = false;
    }

    private void ApplyGestureSelection(int? endIndex)
    {
        _selectedIndices.Clear();
        _selectedIndices.UnionWith(PdfTextSelectionLogic.ApplyGesture(
            _words, _gesturePreviousSelection, _anchorIndex, endIndex,
            _extensionAnchorIndex, _didDrag, _gestureAdditive, _gestureExtend));
        UpdateSelectionVisuals();
        UpdateStatus();
    }

    private Point ClampToPage(Point point)
    {
        return new Point(
            Math.Clamp(point.X, 0, _pageCanvas.Width),
            Math.Clamp(point.Y, 0, _pageCanvas.Height));
    }

    private void OnSelectAllClick(object? sender, RoutedEventArgs e)
    {
        _selectedIndices.Clear();
        foreach (var word in _words)
        {
            _selectedIndices.Add(word.Index);
        }
        _extensionAnchorIndex = _words.Count > 0 ? _words.Min(word => word.Index) : null;

        UpdateSelectionVisuals();
        UpdateStatus();
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        ClearSelection();
    }

    private void ClearSelection()
    {
        _selectedIndices.Clear();
        _extensionAnchorIndex = null;
        UpdateSelectionVisuals();
        UpdateStatus();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        await CopySelectedTextAsync();
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        var selectedWords = GetSelectedWords();
        var text = BuildSelectedText(selectedWords);
        Close(new PdfTextSelectionResult(selectedWords.Select(word => word.Index).ToList(), text));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void UpdateSelectionVisuals()
    {
        var runs = GetSelectedRuns();
        var geometryGroup = new GeometryGroup();
        foreach (var run in runs)
        {
            geometryGroup.Children.Add(new RectangleGeometry(GetRunBounds(run)));
        }

        _selectionHighlightPath.Data = geometryGroup;
        foreach (var element in _selectionHandleElements)
        {
            _pageCanvas.Children.Remove(element);
        }

        _selectionHandleElements.Clear();
        if (runs.Count > 0)
            AddSelectionHandles(GetRunBounds(runs[0]), GetRunBounds(runs[^1]));
    }

    private List<List<PdfSelectableWord>> GetSelectedRuns()
    {
        var selectedWords = GetSelectedWords();
        var runs = new List<List<PdfSelectableWord>>();
        foreach (var word in selectedWords)
        {
            var currentRun = runs.LastOrDefault();
            var previous = currentRun?.LastOrDefault();
            var sameLine = previous != null && AreOnSameLine(previous, word);
            if (previous == null || word.Index != previous.Index + 1 || !sameLine)
            {
                currentRun = new List<PdfSelectableWord>();
                runs.Add(currentRun);
            }

            currentRun!.Add(word);
        }

        return runs;
    }

    private static bool AreOnSameLine(PdfSelectableWord first, PdfSelectableWord second)
    {
        var firstHeight = Math.Max(first.Height, 4);
        var secondHeight = Math.Max(second.Height, 4);
        var overlapTop = Math.Max(first.Top, second.Top);
        var overlapBottom = Math.Min(first.Top + firstHeight, second.Top + secondHeight);
        var overlap = Math.Max(0, overlapBottom - overlapTop);
        var minimumHeight = Math.Min(firstHeight, secondHeight);
        if (overlap >= minimumHeight * 0.35)
        {
            return true;
        }

        var firstCenter = first.Top + (firstHeight / 2);
        var secondCenter = second.Top + (secondHeight / 2);
        return Math.Abs(firstCenter - secondCenter) <= Math.Max(firstHeight, secondHeight) * 0.55;
    }

    private static Rect GetRunBounds(IReadOnlyCollection<PdfSelectableWord> run)
    {
        var left = run.Min(word => word.Left);
        var top = run.Min(word => word.Top);
        var right = run.Max(word => word.Left + Math.Max(word.Width, 4));
        var bottom = run.Max(word => word.Top + Math.Max(word.Height, 4));
        return new Rect(left, top, right - left, bottom - top);
    }

    private void AddSelectionHandles(Rect startBounds, Rect endBounds)
    {
        var inverseZoom = 1 / Math.Max(_zoomScale, MinimumZoomPercent / 100d);
        var barWidth = 2 * inverseZoom;
        var handleDiameter = 12 * inverseZoom;
        var extension = 2.5 * inverseZoom;
        var startBar = new Rectangle
        {
            Width = barWidth,
            Height = startBounds.Height + (extension * 2),
            Fill = SelectionHandleBrush,
            RadiusX = barWidth / 2,
            RadiusY = barWidth / 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(startBar, startBounds.Left - (barWidth / 2));
        Canvas.SetTop(startBar, startBounds.Top - extension);

        var startKnob = new Ellipse
        {
            Width = handleDiameter,
            Height = handleDiameter,
            Fill = SelectionHandleBrush,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(startKnob, startBounds.Left - (handleDiameter / 2));
        Canvas.SetTop(startKnob, startBounds.Top - handleDiameter);

        var endBar = new Rectangle
        {
            Width = barWidth,
            Height = endBounds.Height + (extension * 2),
            Fill = SelectionHandleBrush,
            RadiusX = barWidth / 2,
            RadiusY = barWidth / 2,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(endBar, endBounds.Right - (barWidth / 2));
        Canvas.SetTop(endBar, endBounds.Top - extension);

        var endKnob = new Ellipse
        {
            Width = handleDiameter,
            Height = handleDiameter,
            Fill = SelectionHandleBrush,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(endKnob, endBounds.Right - (handleDiameter / 2));
        Canvas.SetTop(endKnob, endBounds.Bottom);

        _selectionHandleElements.AddRange([startBar, startKnob, endBar, endKnob]);
        foreach (var element in _selectionHandleElements.TakeLast(4))
        {
            _pageCanvas.Children.Add(element);
        }
    }

    private void UpdateStatus()
    {
        _selectionStatusText.Text = _words.Count == 0
            ? "このページには選択できる文字情報がありません。画像PDFの場合はOCRが必要です。"
            : $"{_selectedIndices.Count} / {_words.Count} 文字を選択中";
    }

    private async Task CopySelectedTextAsync()
    {
        var text = BuildSelectedText(GetSelectedWords());
        if (string.IsNullOrWhiteSpace(text))
        {
            _selectionStatusText.Text = "コピーする文字を選択してください。";
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            _selectionStatusText.Text = "クリップボードを利用できません。";
            return;
        }

        await clipboard.SetTextAsync(text);
        _selectionStatusText.Text = $"{_selectedIndices.Count}文字をコピーしました。";
    }

    private List<PdfSelectableWord> GetSelectedWords()
    {
        return _words
            .Where(word => _selectedIndices.Contains(word.Index))
            .OrderBy(word => word.Index)
            .ToList();
    }

    private static string BuildSelectedText(IEnumerable<PdfSelectableWord> words)
    {
        var selected = words.ToList();
        if (selected.Count == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(selected[0].Text);
        for (var index = 1; index < selected.Count; index++)
        {
            var previous = selected[index - 1];
            var current = selected[index];
            var lineThreshold = Math.Max(previous.Height, current.Height) * 0.7;
            if (Math.Abs(current.Top - previous.Top) > lineThreshold)
            {
                builder.AppendLine();
            }
            else if (current.WordIndex != previous.WordIndex)
            {
                builder.Append(' ');
            }

            builder.Append(current.Text);
        }

        return builder.ToString();
    }
}
