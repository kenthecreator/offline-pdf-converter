using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OfflinePDFConverter.Views;

public sealed class PanelScrollThumb : Thumb
{
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private ScrollViewer? _scroll;
    private ScrollBar? _bar;
    private bool _dragging;

    public PanelScrollThumb()
    {
        _idle.Tick += (_, _) => Hide();
        Hide();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _bar = this.FindAncestorOfType<ScrollBar>();
        PseudoClasses.Set(":vertical", _bar?.Orientation == Orientation.Vertical);
        PseudoClasses.Set(":horizontal", _bar?.Orientation == Orientation.Horizontal);
        _scroll = _bar?.TemplatedParent as ScrollViewer;
        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrollChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scroll is not null)
            _scroll.ScrollChanged -= OnScrollChanged;
        _scroll = null;
        _bar = null;
        _dragging = false;
        Hide();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, _scroll))
            return;

        var delta = _bar?.Orientation == Orientation.Horizontal ? args.OffsetDelta.X : args.OffsetDelta.Y;
        if (delta != 0)
            Show();
    }

    protected override void OnDragStarted(VectorEventArgs e)
    {
        base.OnDragStarted(e);
        _dragging = true;
        Show();
    }

    protected override void OnDragCompleted(VectorEventArgs e)
    {
        base.OnDragCompleted(e);
        _dragging = false;
        Show();
    }

    private void Show()
    {
        IsHitTestVisible = true;
        Opacity = 1;
        _idle.Stop();
        if (!_dragging)
            _idle.Start();
    }

    private void Hide()
    {
        _idle.Stop();
        IsHitTestVisible = false;
        Opacity = 0;
    }
}
