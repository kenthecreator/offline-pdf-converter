using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OfflinePDFConverter.Views;

// Preserve the Fluent button presenter contract and add the optical rim in its local coordinates.
internal sealed class ButtonGlass : Control
{
    private static readonly Pen DarkReflection = new(Glint(177, 32, 90), 1.25);
    private static readonly Pen DarkShadow = new(Shade(29, 15, 105), 1.15);
    private static readonly Pen LightEdge = new(Shade(86, 48, 119), 1.05);
    private static readonly Pen LightReflection = new(Glint(250, 94, 184), 1.5);
    private static readonly Pen LightShadow = new(Shade(39, 18, 78), 1.1);

    private static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        TemplatedControl.CornerRadiusProperty.AddOwner<ButtonGlass>();
    private static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<ButtonGlass, IBrush?>("SurfaceBrush");
    private static readonly AttachedProperty<bool> DecorateProperty =
        AvaloniaProperty.RegisterAttached<ButtonGlass, Border, bool>("Decorate");

    static ButtonGlass()
    {
        AffectsRender<ButtonGlass>(CornerRadiusProperty, SurfaceBrushProperty, IsEffectivelyEnabledProperty);
        DecorateProperty.Changed.AddClassHandler<Border>(OnDecorateChanged);
    }

    private readonly GlassOptics? _optics;
    private readonly Border? _surfaceOwner;
    private int _lensFailed;
    private int _lensDrawn;

    private ButtonGlass(GlassOptics? optics = null, Border? surfaceOwner = null)
    {
        _optics = optics;
        _surfaceOwner = surfaceOwner;
        IsHitTestVisible = false;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    internal static ButtonGlass ForProgressBorder(Border surface, GlassOptics optics)
    {
        var rim = new ButtonGlass(optics, surface);
        rim.Bind(CornerRadiusProperty, new Binding(nameof(Border.CornerRadius)) { Source = surface });
        rim.Bind(SurfaceBrushProperty, new Binding(nameof(Border.Background)) { Source = surface });
        return rim;
    }

    internal bool HasRendered => Volatile.Read(ref _lensDrawn) != 0 && Volatile.Read(ref _lensFailed) == 0;

    internal static IEnumerable<Style> CreateStyles(GlassOptics optics)
    {
        yield return CreateButtonStyle(optics);
        yield return DecorateBorder(selector => selector.OfType<ComboBox>().Template().OfType<Border>().Name("Background"));
        yield return DecorateBorder(selector => selector.OfType<ComboBox>().Template().OfType<Border>().Name("HighlightBackground"));
        yield return DecorateBorder(selector => selector.OfType<CheckBox>().Template().OfType<Border>().Name("NormalRectangle"));
        yield return DecorateRadioCircle("OuterEllipse");
        yield return DecorateRadioCircle("CheckOuterEllipse");
    }

    private static Style DecorateBorder(Func<Selector?, Selector> selector)
    {
        var style = new Style(selector);
        style.Setters.Add(new Setter(DecorateProperty, true));
        return style;
    }

    private static Style DecorateRadioCircle(string name)
    {
        var style = new Style(selector => selector.OfType<RadioButton>().Template().OfType<Ellipse>().Name(name));
        style.Setters.Add(new Setter(Shape.StrokeProperty, Gradient(
            Color.FromArgb(225, 248, 248, 248), Color.FromArgb(190, 120, 120, 120), Color.FromArgb(225, 53, 53, 53))));
        return style;
    }

    private static void OnDecorateChanged(Border border, AvaloniaPropertyChangedEventArgs _)
    {
        if (border.GetValue(DecorateProperty) && border.Child is null)
        {
            var rim = new ButtonGlass { IsHitTestVisible = false, Focusable = false };
            rim.Bind(CornerRadiusProperty, new Binding(nameof(Border.CornerRadius)) { Source = border });
            border.Child = rim;
        }
        else if (!border.GetValue(DecorateProperty) && border.Child is ButtonGlass)
        {
            border.Child = null;
        }
    }

    private static IBrush Glint(byte start, byte middle, byte end) => Gradient(
        Color.FromArgb(start, 255, 255, 255), Color.FromArgb(middle, 255, 255, 255), Color.FromArgb(end, 255, 255, 255));

    private static IBrush Shade(byte start, byte middle, byte end) => Gradient(
        Color.FromArgb(start, 0, 0, 0), Color.FromArgb(middle, 0, 0, 0), Color.FromArgb(end, 0, 0, 0));

    private static LinearGradientBrush Gradient(Color start, Color middle, Color end) => new()
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops = new GradientStops { new(start, 0), new(middle, .48), new(end, 1) }
    };

    private static Style CreateButtonStyle(GlassOptics optics)
    {
        var style = new Style(selector => selector.OfType<Button>());
        style.Setters.Add(new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<Button>((button, scope) =>
            {
                // Labeled actions share the pill silhouette. Thumbnail and swatch content keeps its own shape.
                var usePill = button.Content is string { Length: > 0 } || button.CornerRadius.TopLeft >= 100;
                var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
                scope.Register("PART_ContentPresenter", presenter);
                presenter.Bind(ContentPresenter.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.BorderThicknessProperty, new TemplateBinding(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
                if (usePill)
                    presenter.SetValue(ContentPresenter.CornerRadiusProperty, new CornerRadius(999), BindingPriority.Template);
                else
                    presenter.Bind(ContentPresenter.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new TemplateBinding(ContentControl.VerticalContentAlignmentProperty), BindingPriority.Template);
                var rim = new ButtonGlass(usePill ? optics : null) { IsHitTestVisible = false, Focusable = false };
                rim.Bind(CornerRadiusProperty, new Binding(nameof(ContentPresenter.CornerRadius)) { Source = presenter });
                if (usePill)
                    rim.Bind(SurfaceBrushProperty, new Binding(nameof(ContentPresenter.Background)) { Source = presenter });
                return new Grid { Children = { presenter, rim } };
            })));
        return style;
    }

    internal static bool IsAppliedTo(Button button) =>
        button.GetVisualChildren().OfType<Grid>()
            .SelectMany(grid => grid.Children.OfType<ButtonGlass>()).Any(rim => rim.HasDrawableBounds);

    internal static bool HasRenderedPill(Button button) =>
        button.GetVisualChildren().OfType<Grid>()
            .SelectMany(grid => grid.Children.OfType<ButtonGlass>())
            .Any(rim => rim.HasRendered);

    private bool HasDrawableBounds => Bounds.Width >= 8 && Bounds.Height >= 8;

    public override void Render(DrawingContext context)
    {
        if (!HasDrawableBounds)
            return;

        var rect = new Rect(Bounds.Size);
        using var disabledFade = context.PushOpacity(IsEffectivelyEnabled ? 1 : .28);
        if (_optics is not null && Volatile.Read(ref _lensFailed) == 0 &&
            GetValue(SurfaceBrushProperty) is ISolidColorBrush surface)
        {
            var backdrop = ResolveBackdrop();
            var face = Blend(surface.Color, backdrop);
            if (_optics.Draw(context, rect.Size, rect.Size, default, backdrop, face, face, rimOnly: true, OnLensDrawn))
                return;
        }

        var radius = GetValue(CornerRadiusProperty);
        double limit = Math.Min(rect.Width, rect.Height) / 2;
        var outline = new RoundedRect(rect, new CornerRadius(
            Math.Min(radius.TopLeft, limit), Math.Min(radius.TopRight, limit),
            Math.Min(radius.BottomRight, limit), Math.Min(radius.BottomLeft, limit)));
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            DrawRim(context, outline, 1.3, DarkReflection);
            DrawRim(context, outline, 2.55, DarkShadow);
        }
        else
        {
            DrawRim(context, outline, .95, LightEdge);
            DrawRim(context, outline, 2, LightReflection);
            DrawRim(context, outline, 3.35, LightShadow);
        }
    }

    private Color ResolveBackdrop()
    {
        var background = ActualThemeVariant == ThemeVariant.Dark ? Color.FromRgb(25, 25, 25) : Color.FromRgb(242, 242, 242);
        foreach (var visual in this.GetVisualAncestors().Reverse())
        {
            if (visual == _surfaceOwner)
                continue;

            var brush = visual switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                Window window => window.Background,
                _ => null
            };
            if (brush is ISolidColorBrush solid)
                background = Blend(solid.Color, background);
        }
        return background;
    }

    private static Color Blend(Color foreground, Color background)
    {
        int alpha = foreground.A;
        byte Mix(byte front, byte back) => (byte)((front * alpha + back * (255 - alpha) + 127) / 255);
        return Color.FromRgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    private void OnLensDrawn(bool success)
    {
        if (success)
            Volatile.Write(ref _lensDrawn, 1);
        else if (Interlocked.Exchange(ref _lensFailed, 1) == 0)
            Dispatcher.UIThread.Post(InvalidateVisual);
    }

    private static void DrawRim(DrawingContext context, RoundedRect outline, double inset, Pen pen) =>
        context.DrawRectangle(null, pen, outline.Deflate(inset, inset));
}
