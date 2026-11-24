using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

internal sealed class SettingsScrollGlass : Control, IDisposable
{
    internal const double BandDepth = 18;
    private static readonly TimeSpan ScrollIdleDelay = TimeSpan.FromMilliseconds(180);

    private readonly ScrollViewer _scroll;
    private readonly Border _panel;
    private readonly GlassOptics _optics;
    private readonly SolidColorBrush? _panelBrush;
    private readonly bool _captureAfterIdle;
    private IDisposable? _pendingCapture;
    private Vector _snapshotDpi;
    private RenderTargetBitmap? _snapshot;
    private SKShader? _scene;
    private Rect _viewport;
    private double _drawableWidth;
    private Vector _capturedOffset;
    private Size _capturedViewport;
    private bool _captureQueued;
    private bool _visible;
    private bool _disposed;

    public SettingsScrollGlass(ScrollViewer scroll, Border panel, GlassOptics optics, bool captureAfterIdle = false)
    {
        _scroll = scroll;
        _panel = panel;
        _optics = optics;
        _captureAfterIdle = captureAfterIdle;
        _panelBrush = panel.Background as SolidColorBrush;
        IsHitTestVisible = false;
        Focusable = false;

        _scroll.ScrollChanged += OnScrollChanged;
        _scroll.LayoutUpdated += OnLayoutUpdated;
        _scroll.AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        _scroll.AddHandler(TextBox.TextChangedEvent, OnTextChanged, handledEventsToo: true);
        _scroll.AddHandler(Button.ClickEvent, OnClick, handledEventsToo: true);
        _optics.Background.PropertyChanged += OnPaletteChanged;
        if (_panelBrush is not null)
            _panelBrush.PropertyChanged += OnPaletteChanged;
    }

    internal bool HasBackdrop => _pendingCapture is null && _scene is not null && _capturedOffset == _scroll.Offset;

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (ReferenceEquals(args.Source, _scroll))
            QueueCapture(restartIdleTimer: true);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs args)
    {
        var y = args.GetPosition(this).Y;
        if (y <= _viewport.Top + BandDepth || y >= _viewport.Bottom - BandDepth)
            QueueCapture();
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs args) => QueueCapture();

    private void OnClick(object? sender, RoutedEventArgs args) => QueueCapture();

    private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == SolidColorBrush.ColorProperty)
            QueueCapture();
    }

    private void OnLayoutUpdated(object? sender, EventArgs args)
    {
        bool visible = _scroll.IsEffectivelyVisible;
        if (visible && (!_visible || _capturedOffset != _scroll.Offset || _capturedViewport != _scroll.Viewport))
            QueueCapture();
        _visible = visible;
    }

    private void QueueCapture(bool restartIdleTimer = false)
    {
        if (_disposed || _captureQueued || !_scroll.IsEffectivelyVisible)
            return;
        if (_captureAfterIdle)
        {
            if (_pendingCapture is not null && !restartIdleTimer)
                return;
            _pendingCapture?.Dispose();
            _pendingCapture = DispatcherTimer.RunOnce(Capture, ScrollIdleDelay, DispatcherPriority.Background);
            InvalidateVisual();
            return;
        }

        _captureQueued = true;
        // Capture after the ScrollViewer has arranged its content at the new offset.
        Dispatcher.UIThread.Post(Capture, DispatcherPriority.Background);
    }

    private void Capture()
    {
        _captureQueued = false;
        _pendingCapture = null;
        if (_disposed || !_scroll.IsEffectivelyVisible ||
            _scroll.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault() is not { } presenter ||
            presenter.TranslatePoint(default, this) is not { } point ||
            presenter.Bounds.Width < 2 || presenter.Bounds.Height < 2)
            return;

        try
        {
            var viewport = new Rect(point, presenter.Bounds.Size);
            double scale = _captureAfterIdle ? Math.Max(1.5, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) : 1;
            var dpi = new Vector(96 * scale, 96 * scale);
            var pixels = PixelSize.FromSize(viewport.Size, scale);
            if (_snapshot?.PixelSize != pixels || _snapshotDpi != dpi)
            {
                _snapshot?.Dispose();
                _snapshot = new RenderTargetBitmap(pixels, dpi);
                _snapshotDpi = dpi;
            }
            _snapshot.Render(presenter);
            using var bitmap = new SKBitmap(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var buffer = new CaptureBuffer(bitmap, pixels, dpi);
            _snapshot.CopyPixels(buffer, AlphaFormat.Premul);
            bitmap.SetImmutable();
            using var image = SKImage.FromBitmap(bitmap);
            var texture = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale((float)(1 / scale), (float)(1 / scale)));

            _scene?.Dispose();
            _scene = texture;
            _viewport = viewport;
            _capturedOffset = _scroll.Offset;
            _capturedViewport = _scroll.Viewport;
            _drawableWidth = viewport.Width;
            var scrollbar = _scroll.GetVisualDescendants().OfType<ScrollBar>()
                .FirstOrDefault(bar => bar.TemplatedParent == _scroll &&
                                       bar.Orientation == Orientation.Vertical && bar.IsEffectivelyVisible);
            if (scrollbar?.TranslatePoint(default, this) is { } barStart)
                _drawableWidth = Math.Clamp(barStart.X - viewport.X, 0, viewport.Width);
            InvalidateVisual();
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            System.Diagnostics.Trace.WriteLine($"Settings scroll glass could not capture the viewport: {error.Message}");
            Dispose();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_disposed || _pendingCapture is not null || _scene is null || _drawableWidth <= 0 || _capturedOffset != _scroll.Offset)
            return;

        // The viewport is inside the panel's padding, over its solid-color interior.
        var backdrop = _optics.Background.Color;
        var panel = (_panel.Background as ISolidColorBrush)?.Color ?? Colors.Transparent;
        byte Blend(byte front, byte back) => (byte)((front * panel.A + back * (255 - panel.A) + 127) / 255);
        var surface = Color.FromRgb(Blend(panel.R, backdrop.R), Blend(panel.G, backdrop.G), Blend(panel.B, backdrop.B));
        var band = Math.Min(BandDepth, _viewport.Height / 2);
        if (_captureAfterIdle && band < BandDepth)
            return;

        float top = EdgeStrength(_scroll.Offset.Y);
        float bottom = EdgeStrength(_scroll.Extent.Height - _scroll.Viewport.Height - _scroll.Offset.Y);
        if ((top > 0 || bottom > 0) &&
            !_optics.DrawScrollEdges(context, _viewport, _drawableWidth, band, _scene, surface, top, bottom, OnDraw, stableSampling: _captureAfterIdle))
            Dispose();

        float EdgeStrength(double hiddenHeight)
        {
            var progress = (float)Math.Clamp((hiddenHeight - .5) / band, 0, 1);
            return progress * progress * (3 - 2 * progress);
        }
    }

    private void OnDraw(bool success)
    {
        if (!success)
            Dispatcher.UIThread.Post(Dispose);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pendingCapture?.Dispose();
        _pendingCapture = null;
        _scroll.ScrollChanged -= OnScrollChanged;
        _scroll.LayoutUpdated -= OnLayoutUpdated;
        _scroll.RemoveHandler(PointerMovedEvent, OnPointerMoved);
        _scroll.RemoveHandler(TextBox.TextChangedEvent, OnTextChanged);
        _scroll.RemoveHandler(Button.ClickEvent, OnClick);
        _optics.Background.PropertyChanged -= OnPaletteChanged;
        if (_panelBrush is not null)
            _panelBrush.PropertyChanged -= OnPaletteChanged;
        _scene?.Dispose();
        _scene = null;
        _snapshot?.Dispose();
        _snapshot = null;
        InvalidateVisual();
    }

    private sealed class CaptureBuffer(SKBitmap bitmap, PixelSize size, Vector dpi) : ILockedFramebuffer
    {
        public IntPtr Address => bitmap.GetPixels();
        public PixelSize Size => size;
        public int RowBytes => bitmap.RowBytes;
        public Vector Dpi => dpi;
        public PixelFormat Format => PixelFormat.Bgra8888;
        public void Dispose() { }
    }
}
