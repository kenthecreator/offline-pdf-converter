using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Diagnostics;
using OfflinePDFConverter.Models;
using OfflinePDFConverter.Services;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private Border _ocrPreviewSelectorSwitch = null!;
    private RadioButton _ocrPreviewIconButton = null!;
    private RadioButton _ocrPreviewListButton = null!;
    private TranslateTransform _ocrPreviewSelectionTransform = null!;
    private Transitions? _ocrPreviewSelectionTransitions;
    private bool _ocrPreviewDragActive, _ocrPreviewDragMoved, _isOcrPreviewListView, _syncingOcrScroll;
    private double _ocrPreviewDragStartPointerX, _ocrPreviewDragStartTransformX, _ocrScrollFraction;
    private readonly Stopwatch _ocrScanWatch = new();
    private readonly DispatcherTimer _ocrScanTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private List<PdfPagePreviewItem> _ocrScanningPages = new();
    private readonly Stopwatch _ocrResultWatch = new();
    private readonly DispatcherTimer _ocrResultTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    private void InitializeOcrPreviewMotion()
    {
        _ocrPreviewSelectorSwitch = Required<Border>("OcrPreviewSelectorSwitch");
        _ocrPreviewIconButton = Required<RadioButton>("OcrPreviewIconButton");
        _ocrPreviewListButton = Required<RadioButton>("OcrPreviewListButton");
        _ocrPreviewSelectionTransform = (TranslateTransform)Required<Border>("OcrPreviewSelectionPill").RenderTransform!;
        _ocrPreviewSelectionTransitions = _ocrPreviewSelectionTransform.Transitions;
        _ocrPreviewSelectorSwitch.AddHandler(PointerPressedEvent, OnOcrPreviewSelectorPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _ocrPreviewSelectorSwitch.AddHandler(KeyDownEvent, OnOcrPreviewSelectorKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        _ocrPreviewSelectorSwitch.PointerMoved += OnOcrPreviewSelectorPointerMoved;
        _ocrPreviewSelectorSwitch.PointerReleased += OnOcrPreviewSelectorPointerReleased;
        _ocrPreviewSelectorSwitch.PointerCaptureLost += OnOcrPreviewSelectorPointerCaptureLost;
        foreach (var side in new[] { "Before", "After" })
            foreach (var mode in new[] { "Icon", "List" })
                Required<ScrollViewer>($"Ocr{side}{mode}Scroll").ScrollChanged += OnOcrPreviewScrollChanged;
        _ocrResultTimer.Tick += (_, _) =>
        {
            var progress = _ocrResultWatch.Elapsed.TotalSeconds / 10 % 1;
            foreach (var page in _ocrAfterPages) page.OcrResultPosition = progress;
        };
        _ocrScanTimer.Tick += (_, _) =>
        {
            if (_reduceMotion) return;
            var phase = _ocrScanWatch.Elapsed.TotalSeconds / 1.8 % 2;
            var position = phase <= 1 ? phase : 2 - phase;
            foreach (var page in _ocrScanningPages) page.OcrScanPosition = position;
        };
    }

    private void OnOcrPreviewScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncingOcrScroll || sender is not ScrollViewer source || !source.IsVisible) return;
        var maximum = Math.Max(0, source.Extent.Height - source.Viewport.Height);
        if (Math.Abs(e.OffsetDelta.Y) > .01 && maximum > 0)
            _ocrScrollFraction = Math.Clamp(source.Offset.Y / maximum, 0, 1);
        ApplyOcrScrollPosition();
    }

    private void ApplyOcrScrollPosition()
    {
        if (_syncingOcrScroll) return;
        _syncingOcrScroll = true;
        try
        {
            foreach (var side in new[] { "Before", "After" })
            {
                var scroll = Required<ScrollViewer>($"Ocr{side}{(_isOcrPreviewListView ? "List" : "Icon")}Scroll");
                var y = _ocrScrollFraction * Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
                if (Math.Abs(scroll.Offset.Y - y) > .5) scroll.Offset = new Vector(0, y);
            }
        }
        finally { _syncingOcrScroll = false; }
    }

    private async Task PrepareOcrScanAsync(string path, string pageSelection, string? password, CancellationToken token)
    {
        StopOcrScan();
        if (_ocrBeforePages.Count == 0 || _ocrBeforePages[0].PdfPath != path)
        {
            var pages = await Task.Run(() => LoadOcrPreviews(path, string.IsNullOrEmpty(password) ? null : password, token), token);
            if (token.IsCancellationRequested) { DisposeOcrPages(pages); token.ThrowIfCancellationRequested(); }
            SetOcrPreviewPages("Before", pages);
        }
        var selected = string.IsNullOrWhiteSpace(pageSelection)
            ? _ocrBeforePages.Select(p => p.PageNumber).ToHashSet()
            : PageRangeParser.Parse(pageSelection, _ocrBeforePages.Count, "認識する").ToHashSet();
        _ocrFilesList.SelectedItem = _pdfFiles.FirstOrDefault(file => file.Path == path);
        SetOcrPreviewPages("After", new());
        Required<TextBlock>("OcrBeforeHint").Text = $"{selected.Count} / {_ocrBeforePages.Count}ページをOCR処理中";
        Required<TextBlock>("OcrAfterHint").Text = "OCR処理後に表示します。";
        _ocrScanningPages = _ocrBeforePages.Where(p => selected.Contains(p.PageNumber)).ToList();
        foreach (var page in _ocrScanningPages) page.OcrScanPosition = _reduceMotion ? .5 : 0;
        _ocrScanWatch.Restart(); _ocrScanTimer.Start();
        Dispatcher.UIThread.Post(() =>
        {
            var items = Required<ItemsControl>($"OcrBefore{(_isOcrPreviewListView ? "List" : "Icon")}Items");
            items.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(button => button.DataContext == _ocrScanningPages.FirstOrDefault())?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }

    private async Task WaitForOcrScanAsync(CancellationToken token)
    {
        if (_ocrScanningPages.Count == 0 || token.IsCancellationRequested) return;
        var remaining = TimeSpan.FromSeconds(5) - _ocrScanWatch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            SetStatus("処理結果を準備しています…");
            try { await Task.Delay(remaining, token); }
            catch (OperationCanceledException) { /* Keep completed file results when cancelling only the visual delay. */ }
        }
    }

    private void StopOcrScan()
    {
        _ocrScanTimer.Stop(); _ocrScanWatch.Stop();
        foreach (var page in _ocrScanningPages) page.OcrScanPosition = -1;
        _ocrScanningPages.Clear();
    }

    private void BeginOcrResultAnimation()
    {
        StopOcrScan();
        foreach (var page in _ocrAfterPages) page.OcrResultPosition = _reduceMotion ? 1 : 0;
        if (_reduceMotion) return;
        _ocrResultWatch.Restart(); _ocrResultTimer.Start();
    }

    private void StopOcrResultAnimation()
    {
        _ocrResultTimer.Stop(); _ocrResultWatch.Stop();
        foreach (var page in _ocrAfterPages) page.OcrResultPosition = 1;
    }

    private void OnOcrPreviewIconClick(object? sender, RoutedEventArgs e)
    {
        SetOcrPreviewDisplay(useListView: false);
    }

    private void OnOcrPreviewListClick(object? sender, RoutedEventArgs e)
    {
        SetOcrPreviewDisplay(useListView: true);
    }

    private void OnOcrPreviewSelectorKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
            case Key.Home:
                SetOcrPreviewDisplay(useListView: false);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Down:
            case Key.End:
                SetOcrPreviewDisplay(useListView: true);
                e.Handled = true;
                break;
        }
    }

    private void OnOcrPreviewSelectorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_ocrPreviewSelectorSwitch).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _ocrPreviewDragActive = true;
        _ocrPreviewDragMoved = false;
        _ocrPreviewDragStartPointerX = e.GetPosition(_ocrPreviewSelectorSwitch).X;
        _ocrPreviewDragStartTransformX = _ocrPreviewSelectionTransform.X;
        _ocrPreviewSelectionTransform.Transitions = null;
        e.Pointer.Capture(_ocrPreviewSelectorSwitch);
        e.Handled = true;
    }

    private void OnOcrPreviewSelectorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_ocrPreviewDragActive)
        {
            return;
        }

        var delta = e.GetPosition(_ocrPreviewSelectorSwitch).X - _ocrPreviewDragStartPointerX;
        _ocrPreviewDragMoved |= Math.Abs(delta) >= DragActivationDistance;
        _ocrPreviewSelectionTransform.X = Math.Clamp(
            _ocrPreviewDragStartTransformX + delta,
            0,
            PreviewViewDragMaximum);
        e.Handled = true;
    }

    private void OnOcrPreviewSelectorPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_ocrPreviewDragActive)
        {
            return;
        }

        var releaseX = e.GetPosition(_ocrPreviewSelectorSwitch).X;
        var useListView = _ocrPreviewDragMoved
            ? _ocrPreviewSelectionTransform.X >= PreviewViewDragMaximum / 2
            : releaseX >= _ocrPreviewSelectorSwitch.Bounds.Width / 2;
        FinishOcrPreviewDrag(useListView);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnOcrPreviewSelectorPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_ocrPreviewDragActive)
        {
            FinishOcrPreviewDrag(_ocrPreviewSelectionTransform.X >= PreviewViewDragMaximum / 2);
        }
    }

    private void FinishOcrPreviewDrag(bool useListView)
    {
        _ocrPreviewDragActive = false;
        _ocrPreviewSelectionTransform.Transitions = _ocrPreviewSelectionTransitions;
        SetOcrPreviewDisplay(useListView);
    }

    private void SetOcrPreviewDisplay(bool useListView)
    {
        _isOcrPreviewListView = useListView;
        UpdateOcrPreviewDisplay();
        (useListView ? _ocrPreviewListButton : _ocrPreviewIconButton).Focus();
    }

}
