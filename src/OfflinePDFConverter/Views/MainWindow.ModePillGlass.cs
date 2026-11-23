using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Threading;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private ModePillGlass? _modePillGlass;

    internal GlassOptics? PillOptics { get; private set; }

    private static readonly (string Pill, string Track)[] GlassSelectors =
    {
        ("ModeSelectionPill", "ModeSelectorTrack"),
        ("ThemeToggleThumb", "ThemeSelectorTrack"),
        ("DirectionSelectionPill", "DirectionSelectorTrack"),
        ("PdfPreviewSelectionPill", "PdfPreviewSelectorTrack"),
        ("OcrPreviewSelectionPill", "OcrPreviewSelectorTrack")
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
        _mainProgressBar.TemplateApplied += OnProgressTemplateApplied;
        var dpiTrack = Required<Border>("PdfDpiTrack");
        if (dpiTrack.Child is Panel dpiTrackLayers)
            dpiTrackLayers.Children.Insert(0, ButtonGlass.ForBorder(dpiTrack, effect));
        _pdfDpiRange.Child = ButtonGlass.ForBorder(_pdfDpiRange, effect);
        _pdfDpiThumb.Child = ButtonGlass.ForBorder(_pdfDpiThumb, effect);

        var lenses = new List<ModePillGlass>();
        foreach (var (pillName, trackName) in GlassSelectors)
        {
            var pill = Required<Border>(pillName);
            if (pill.RenderTransform is not TranslateTransform translation)
                continue;
            var lens = new ModePillGlass(Required<Border>(trackName), pill, translation,
                background, trackColor, pillColor, effect);
            lenses.Add(lens);
            if (pillName == "ModeSelectionPill")
                _modePillGlass = lens;
        }
        Closed += (_, _) =>
        {
            _mainProgressBar.TemplateApplied -= OnProgressTemplateApplied;
            foreach (var lens in lenses)
                lens.Dispose();
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
        private int _disabled;
        private int _rendered;
        private bool _disposed;

        public bool IsAvailable => Volatile.Read(ref _disabled) == 0;
        public bool HasRendered => Volatile.Read(ref _rendered) != 0;

        public ModePillGlass(Border track, Border pill, TranslateTransform translation,
            SolidColorBrush background, SolidColorBrush trackColor, SolidColorBrush pillColor, GlassOptics effect)
        {
            _track = track;
            _pill = pill;
            _translation = translation;
            _background = background;
            _trackColor = trackColor;
            _pillColor = pillColor;
            _effect = effect;
            _pillDecoration = new GlassDecoration(this);

            _pill.Child = _pillDecoration;
            _track.PropertyChanged += OnBoundsChanged;
            _pill.PropertyChanged += OnBoundsChanged;
            _translation.Changed += OnTranslationChanged;
            _background.PropertyChanged += OnPaletteChanged;
            _trackColor.PropertyChanged += OnPaletteChanged;
            _pillColor.PropertyChanged += OnPaletteChanged;
        }

        private void OnBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == BoundsProperty)
                _pillDecoration.InvalidateVisual();
        }

        private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == SolidColorBrush.ColorProperty)
                _pillDecoration.InvalidateVisual();
        }

        private void OnTranslationChanged(object? sender, EventArgs args) => _pillDecoration.InvalidateVisual();

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
            if (!_effect.Draw(context, size, trackBounds.Size, origin, _background.Color,
                    _trackColor.Color, _pillColor.Color, rimOnly: false, OnDraw))
                OnDraw(false);
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
            Interlocked.Exchange(ref _disabled, 1);
            _track.PropertyChanged -= OnBoundsChanged;
            _pill.PropertyChanged -= OnBoundsChanged;
            _translation.Changed -= OnTranslationChanged;
            _background.PropertyChanged -= OnPaletteChanged;
            _trackColor.PropertyChanged -= OnPaletteChanged;
            _pillColor.PropertyChanged -= OnPaletteChanged;
            _pill.Child = null;
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
