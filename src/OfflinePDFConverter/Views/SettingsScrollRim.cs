using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace OfflinePDFConverter.Views;

// Draw only the rim's reflection. Scrolled content remains in the ScrollViewer's normal render pass.
internal sealed class SettingsScrollRim : Control, IDisposable
{
    private const double Depth = 6;
    private static readonly IBrush LightTop = CreateBrush("#30FFFFFF", "#18000000", top: true);
    private static readonly IBrush LightBottom = CreateBrush("#24000000", "#38FFFFFF", top: false);
    private static readonly IBrush DarkTop = CreateBrush("#32FFFFFF", "#30000000", top: true);
    private static readonly IBrush DarkBottom = CreateBrush("#42000000", "#20FFFFFF", top: false);
    private readonly ScrollViewer _scroll;

    internal double TopStrength { get; private set; }
    internal double BottomStrength { get; private set; }

    internal SettingsScrollRim(ScrollViewer scroll)
    {
        _scroll = scroll;
        IsHitTestVisible = false;
        Focusable = false;
        _scroll.ScrollChanged += OnScrollChanged;
        ActualThemeVariantChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs args) => InvalidateVisual();

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, _scroll))
            return;

        double top = Strength(_scroll.Offset.Y);
        double bottom = Strength(_scroll.Extent.Height - _scroll.Viewport.Height - _scroll.Offset.Y);
        if (top == TopStrength && bottom == BottomStrength)
            return;

        TopStrength = top;
        BottomStrength = bottom;
        InvalidateVisual();
    }

    private static double Strength(double distance) => Math.Clamp((distance - .5) / Depth, 0, 1);

    public override void Render(DrawingContext context)
    {
        double depth = Math.Min(Depth, Bounds.Height / 2);
        // Keep the reflection off the scrollbar and the panel's rounded corners.
        double width = Bounds.Width - 20;
        if (depth <= 0 || width <= 0)
            return;

        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        if (TopStrength > 0)
        {
            using var opacity = context.PushOpacity(TopStrength);
            context.DrawRectangle(dark ? DarkTop : LightTop, null, new Rect(6, 0, width, depth));
        }
        if (BottomStrength > 0)
        {
            using var opacity = context.PushOpacity(BottomStrength);
            context.DrawRectangle(dark ? DarkBottom : LightBottom, null, new Rect(6, Bounds.Height - depth, width, depth));
        }
    }

    private static IBrush CreateBrush(string edge, string shoulder, bool top) => new LinearGradientBrush
    {
        StartPoint = top ? RelativePoint.TopLeft : new RelativePoint(0, 1, RelativeUnit.Relative),
        EndPoint = top ? new RelativePoint(0, 1, RelativeUnit.Relative) : RelativePoint.TopLeft,
        GradientStops = new GradientStops
        {
            new(Color.Parse(edge), 0),
            new(Color.Parse(shoulder), .35),
            new(Colors.Transparent, 1)
        }
    };

    public void Dispose()
    {
        _scroll.ScrollChanged -= OnScrollChanged;
        ActualThemeVariantChanged -= OnThemeChanged;
    }
}
