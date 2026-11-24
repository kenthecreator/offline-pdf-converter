using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private readonly Dictionary<string, ModePillGlass> _selectorLenses = new();

    internal GlassOptics? PillOptics { get; private set; }

    private static readonly (string Pill, string Track, string Labels, double Step)[] GlassSelectors =
    {
        ("ModeSelectionPill", "ModeSelectorTrack", "ModeSelectorButtons", ModeSegmentWidth),
        ("ThemeToggleThumb", "ThemeSelectorTrack", "ThemeSelectorLabels", ThemeSegmentWidth),
        ("DirectionSelectionPill", "DirectionSelectorTrack", "DirectionSelectorButtons", DirectionSegmentWidth),
        ("PdfPreviewSelectionPill", "PdfPreviewSelectorTrack", "PdfPreviewSelectorButtons", PreviewViewDragMaximum),
        ("OcrPreviewSelectionPill", "OcrPreviewSelectorTrack", "OcrPreviewSelectorButtons", PreviewViewDragMaximum)
    };

    private void InitializePillGlass()
    {
        if (Resources["AppBackgroundBrush"] is not SolidColorBrush background ||
            Resources["ModeTrackBrush"] is not SolidColorBrush trackColor ||
            Resources["ModePillBrush"] is not SolidColorBrush pillColor)
            return;

        if (GlassOptics.Compile(Program.GlassTrial, background) is not { } effect)
            return;
        PillOptics = effect;
        bool captureAfterIdle = OperatingSystem.IsWindows();
        var scrollEdges = new List<IDisposable>();
        foreach (var name in new[] { "PdfOutputSettings", "OcrSettings" })
        {
            var viewport = Required<Grid>(name + "Viewport");
            var scroll = Required<ScrollViewer>(name + "Scroll");
            if (captureAfterIdle)
            {
                var rim = new SettingsScrollRim(scroll);
                viewport.Children.Add(rim);
                scrollEdges.Add(rim);
            }

            var edge = new SettingsScrollGlass(scroll, (Border)viewport.Parent!, effect,
                captureAfterIdle);
            viewport.Children.Add(edge);
            scrollEdges.Add(edge);
        }
        _mainProgressBar.TemplateApplied += OnProgressTemplateApplied;
        var dpiTrack = Required<Border>("PdfDpiTrack");
        if (dpiTrack.Child is Panel dpiTrackLayers)
            dpiTrackLayers.Children.Insert(0, ButtonGlass.ForBorder(dpiTrack, effect, completeFace: true));
        _pdfDpiRange.Child = ButtonGlass.ForBorder(_pdfDpiRange, effect, completeFace: true);
        _pdfDpiThumb.Child = ButtonGlass.ForBorder(_pdfDpiThumb, effect, completeFace: true);

        foreach (var (pillName, trackName, labelsName, step) in GlassSelectors)
        {
            var pill = Required<Border>(pillName);
            if (pill.RenderTransform is not TranslateTransform translation)
                continue;
            _selectorLenses.Add(pillName, new ModePillGlass(Required<Border>(trackName), pill, translation,
                background, trackColor, pillColor, effect, Required<Grid>(labelsName), step));
        }
        Closed += (_, _) =>
        {
            _mainProgressBar.TemplateApplied -= OnProgressTemplateApplied;
            foreach (var lens in _selectorLenses.Values)
                lens.Dispose();
            _selectorLenses.Clear();
            foreach (var edge in scrollEdges)
                edge.Dispose();
            PillOptics = null;
            effect.Dispose();
        };
    }

    private void OnProgressTemplateApplied(object? sender, TemplateAppliedEventArgs args)
    {
        if (PillOptics is not { } optics)
            return;

        // Attach inside Fluent's borders so its native width, clipping and animations drive the effect.
        if (args.NameScope.Find("ProgressBarRoot") is Border { Child: Panel track } background)
            track.Children.Insert(0, ButtonGlass.ForBorder(background, optics));

        foreach (var part in new[] { "PART_Indicator", "IndeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator2" })
        {
            if (args.NameScope.Find(part) is Border { Child: null } indicator)
                indicator.Child = ButtonGlass.ForBorder(indicator, optics);
        }
    }

    private sealed class ModePillGlass : IDisposable
    {
        private readonly Border _track;
        private readonly Border _pill;
        private readonly TranslateTransform _translation;
        private readonly SolidColorBrush _background;
        private readonly SolidColorBrush _trackColor;
        private readonly SolidColorBrush _pillColor;
        private readonly GlassOptics _effect;
        private readonly GlassDecoration _pillDecoration;
        private readonly Grid _labels;
        private readonly LabelDecoration? _labelDecoration;
        private int _labelWarpFailed;
        private bool _showWarpedLabels;
        private readonly double _step;
        private readonly RadioButton[] _buttons;
        private readonly TextBlock[] _labelText;
        private SKShader? _labelTexture;
        private bool _labelCaptureQueued;
        private bool _labelCaptureFailed;
        private bool _labelCaptureDirty = true;
        private bool _refractLabels = true;
        private int _disabled;
        private int _rendered;
        private bool _disposed;

        public bool IsAvailable => Volatile.Read(ref _disabled) == 0;
        public bool HasRendered => Volatile.Read(ref _rendered) != 0;
        public bool HasLabelBackdrop => _labelTexture is not null && !_labelCaptureDirty;
        public bool UsesWarpedLabelLayer => _showWarpedLabels;
        public bool LabelsAreLive => _labelText.All(label => label.Opacity == 1);

        private double DistanceFromStop =>
            Math.Abs(_translation.X - Math.Round(_translation.X / _step) * _step);

        public void SetLabelRefraction(bool enabled)
        {
            _refractLabels = enabled;
            _pillDecoration.InvalidateVisual();
            _labelDecoration?.InvalidateVisual();
        }

        public ModePillGlass(Border track, Border pill, TranslateTransform translation,
            SolidColorBrush background, SolidColorBrush trackColor, SolidColorBrush pillColor, GlassOptics effect,
            Grid labels, double step)
        {
            _track = track;
            _pill = pill;
            _translation = translation;
            _background = background;
            _trackColor = trackColor;
            _pillColor = pillColor;
            _effect = effect;
            _pillDecoration = new GlassDecoration(this);
            _labels = labels;
            _step = step;
            _buttons = labels.Children.OfType<RadioButton>().ToArray();
            _labelText = labels.Children.OfType<TextBlock>()
                .Concat(_buttons.Select(button => button.Content).OfType<TextBlock>()).ToArray();

            if (OperatingSystem.IsWindows() && labels.Parent is Panel labelParent)
            {
                _labelDecoration = new LabelDecoration(this);
                Grid.SetColumnSpan(_labelDecoration, Grid.GetColumnSpan(labels));
                labelParent.Children.Add(_labelDecoration);
            }

            _pill.Child = _pillDecoration;
            _track.PropertyChanged += OnBoundsChanged;
            _pill.PropertyChanged += OnBoundsChanged;
            _translation.Changed += OnTranslationChanged;
            _background.PropertyChanged += OnPaletteChanged;
            _trackColor.PropertyChanged += OnPaletteChanged;
            _pillColor.PropertyChanged += OnPaletteChanged;
            _labels.Loaded += OnLabelsLoaded;
            _labels.LayoutUpdated += OnLabelsLayoutUpdated;
            _labels.PropertyChanged += OnLabelChanged;
            foreach (var label in _labelText)
                label.PropertyChanged += OnLabelChanged;
            foreach (var button in _buttons)
                button.PropertyChanged += OnLabelChanged;
        }

        private void OnLabelsLoaded(object? sender, EventArgs args) => InvalidateLabels();

        // A hidden pane can become visible again with unchanged bounds and cached drawing commands.
        private void OnLabelsLayoutUpdated(object? sender, EventArgs args) => QueueLabelCapture();

        private void OnLabelChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == BoundsProperty || args.Property == TextBlock.ForegroundProperty ||
                args.Property == TextBlock.TextProperty || args.Property == TemplatedControl.BorderBrushProperty)
                InvalidateLabels();
        }

        private void InvalidateLabels()
        {
            _labelCaptureDirty = true;
            if (_labelDecoration is not null)
                UpdateLabelLayer();
            QueueLabelCapture();
            _pillDecoration.InvalidateVisual();
        }

        private void QueueLabelCapture()
        {
            if (!_labelCaptureDirty || _labelCaptureQueued || _labelCaptureFailed || _disposed ||
                Volatile.Read(ref _labelWarpFailed) != 0 ||
                !_labels.IsEffectivelyVisible || _labels.Bounds.Width <= 0 || _labels.Bounds.Height <= 0)
                return;
            _labelCaptureQueued = true;
            Dispatcher.UIThread.Post(CaptureLabels, DispatcherPriority.Background);
        }

        private void CaptureLabels()
        {
            _labelCaptureQueued = false;
            if (_disposed || !_labels.IsEffectivelyVisible || _labels.Bounds.Width <= 0 || _labels.Bounds.Height <= 0)
                return;

            try
            {
                const int scale = 2;
                var size = PixelSize.FromSize(_labels.Bounds.Size, scale);
                using var snapshot = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
                if (_labelDecoration is null)
                {
                    snapshot.Render(_labels);
                }
                else
                {
                    // Paint just the native glyphs with grayscale antialiasing on the transparent target.
                    using var drawing = snapshot.CreateDrawingContext();
                    using var renderOptions = drawing.PushRenderOptions(new RenderOptions
                    {
                        TextRenderingMode = TextRenderingMode.Antialias
                    });
                    foreach (var label in _labelText)
                    {
                        if (!label.IsEffectivelyVisible || label.TransformToVisual(_labels) is not { } transform)
                            continue;
                        using var state = drawing.PushTransform(transform);
                        label.Render(drawing);
                    }
                }
                using var encoded = new MemoryStream();
                snapshot.Save(encoded);
                using var image = SKImage.FromEncodedData(encoded.ToArray())
                    ?? throw new InvalidOperationException("Selector labels could not be decoded.");
                var texture = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal,
                    new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale(1f / scale, 1f / scale))
                    ?? throw new InvalidOperationException("Selector labels could not be sampled.");
                _labelTexture?.Dispose();
                _labelTexture = texture;
                _labelCaptureDirty = false;
                UpdateLabelLayer();
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                _labelCaptureFailed = true;
                _labelTexture?.Dispose();
                _labelTexture = null;
                _pill.ClearValue(ZIndexProperty);
                UpdateLabelLayer();
                System.Diagnostics.Trace.WriteLine($"Selector labels could not be captured: {error.Message}");
            }
            _pillDecoration.InvalidateVisual();
        }

        private void OnBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == BoundsProperty)
            {
                _pillDecoration.InvalidateVisual();
                _labelDecoration?.InvalidateVisual();
            }
        }

        private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == SolidColorBrush.ColorProperty)
                InvalidateLabels();
        }

        private void UpdateLabelLayer()
        {
            if (_labelDecoration is { } decoration)
            {
                bool showWarp = !_disposed && HasLabelBackdrop && DistanceFromStop > .5 &&
                    Volatile.Read(ref _labelWarpFailed) == 0;
                if (_showWarpedLabels != showWarp)
                {
                    _showWarpedLabels = showWarp;
                    foreach (var label in _labelText)
                        label.SetCurrentValue(OpacityProperty, showWarp ? 0d : 1d);
                }
                decoration.InvalidateVisual();
                return;
            }

            // At each stop, let Avalonia draw the text directly at its original position.
            int zIndex = _labelTexture is not null && DistanceFromStop > .5 ? _labels.ZIndex + 1 : _labels.ZIndex;
            if (_pill.ZIndex != zIndex)
                _pill.ZIndex = zIndex;
        }

        private void OnTranslationChanged(object? sender, EventArgs args)
        {
            UpdateLabelLayer();
            _pillDecoration.InvalidateVisual();
        }

        private void Render(DrawingContext context, Size size)
        {
            if (Volatile.Read(ref _disabled) != 0 || size.Width <= 0 || size.Height <= 0)
                return;

            var trackBounds = _track.Bounds;
            if (trackBounds.Width <= 0 || trackBounds.Height <= 0)
                return;

            var origin = _pill.Bounds.Position
                + new Vector(_pill.BorderThickness.Left + _translation.X, _pill.BorderThickness.Top)
                - trackBounds.Position;
            QueueLabelCapture();
            var labelsOffset = trackBounds.Position - _labels.Bounds.Position;
            var labels = _pill.ZIndex > _labels.ZIndex ? _labelTexture : null;
            float approach = (float)Math.Clamp((DistanceFromStop - .5) / 12, 0, 1);
            float refraction = _refractLabels ? 1.35f * approach * approach * (3 - 2 * approach) : 0;
            if (!_effect.Draw(context, size, trackBounds.Size, origin, _background.Color,
                    _trackColor.Color, _pillColor.Color, rimOnly: false, OnDraw,
                    labels: labels, labelsOffset: labelsOffset, labelRefraction: refraction,
                    emphasizeTrack: _labelDecoration is not null))
                OnDraw(false);
        }

        private void RenderWarpedLabels(DrawingContext context, Size size)
        {
            if (!UsesWarpedLabelLayer || _labelTexture is not { } texture || size.Width <= 0 || size.Height <= 0)
                return;

            var origin = _pill.Bounds.Position
                + new Vector(_pill.BorderThickness.Left + _translation.X, _pill.BorderThickness.Top)
                - _labels.Bounds.Position;
            float progress = (float)Math.Clamp((DistanceFromStop - .5) / 12, 0, 1);
            float strength = _refractLabels ? progress * progress * (3 - 2 * progress) : 0;
            if (!_effect.Draw(context, _pillDecoration.Bounds.Size, _track.Bounds.Size, origin,
                    _background.Color, _trackColor.Color, _pillColor.Color, rimOnly: false, OnLabelDraw,
                    labels: texture, labelRefraction: strength, labelLayerSize: size))
                OnLabelDraw(false);
        }

        private void OnLabelDraw(bool success)
        {
            if (!success && Interlocked.Exchange(ref _labelWarpFailed, 1) == 0)
                Dispatcher.UIThread.Post(UpdateLabelLayer);
        }

        private void OnDraw(bool success)
        {
            if (success)
                Volatile.Write(ref _rendered, 1);
            else if (Interlocked.Exchange(ref _disabled, 1) == 0)
                Dispatcher.UIThread.Post(Dispose);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_labelDecoration is { } decoration)
            {
                UpdateLabelLayer();
                (decoration.Parent as Panel)?.Children.Remove(decoration);
            }
            Interlocked.Exchange(ref _disabled, 1);
            _track.PropertyChanged -= OnBoundsChanged;
            _pill.PropertyChanged -= OnBoundsChanged;
            _translation.Changed -= OnTranslationChanged;
            _background.PropertyChanged -= OnPaletteChanged;
            _trackColor.PropertyChanged -= OnPaletteChanged;
            _pillColor.PropertyChanged -= OnPaletteChanged;
            _labels.Loaded -= OnLabelsLoaded;
            _labels.LayoutUpdated -= OnLabelsLayoutUpdated;
            _labels.PropertyChanged -= OnLabelChanged;
            foreach (var label in _labelText)
                label.PropertyChanged -= OnLabelChanged;
            foreach (var button in _buttons)
                button.PropertyChanged -= OnLabelChanged;
            _pill.ClearValue(ZIndexProperty);
            _labelTexture?.Dispose();
            _labelTexture = null;
            _pill.Child = null;
        }

        private sealed class LabelDecoration : Control
        {
            private readonly ModePillGlass _owner;

            public LabelDecoration(ModePillGlass owner)
            {
                _owner = owner;
                IsHitTestVisible = false;
                Focusable = false;
            }

            public override void Render(DrawingContext context) => _owner.RenderWarpedLabels(context, Bounds.Size);
        }

        private sealed class GlassDecoration(ModePillGlass owner) : Control
        {
            protected override void OnInitialized()
            {
                base.OnInitialized();
                IsHitTestVisible = false;
                Focusable = false;
            }

            public override void Render(DrawingContext context) => owner.Render(context, Bounds.Size);
        }

    }
}
