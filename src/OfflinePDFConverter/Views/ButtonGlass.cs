using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OfflinePDFConverter.Views;

// Add a directional reflection over the existing themed surface.
internal sealed class ButtonGlass : Control
{
    private static readonly BoxShadows PillShadow = BoxShadows.Parse("0 1 6 0 #0D000000");
    private static readonly IBrush DarkFace = Gradient("#10FFFFFF", "#00FFFFFF", "#12000000");
    private static readonly IBrush LightFace = Gradient("#99FFFFFF", "#00FFFFFF", "#09000000");
    private static readonly Pen DarkEdge = new(Gradient("#49FFFFFF", "#08FFFFFF", "#25000000"), .8);
    private static readonly Pen LightEdge = new(Gradient("#CCFFFFFF", "#08FFFFFF", "#22000000"), .8);
    private static readonly FuncValueConverter<Thickness, Thickness> RimMargin = new(padding =>
        new Thickness(-padding.Left, -padding.Top, -padding.Right, -padding.Bottom));

    private static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        TemplatedControl.CornerRadiusProperty.AddOwner<ButtonGlass>();
    private static readonly StyledProperty<IBrush?> SurfaceBrushProperty =
        AvaloniaProperty.Register<ButtonGlass, IBrush?>("SurfaceBrush");
    private static readonly AttachedProperty<GlassOptics?> DecorateProperty =
        AvaloniaProperty.RegisterAttached<ButtonGlass, Border, GlassOptics?>("Decorate");
    private static readonly AttachedProperty<GlassOptics?> DecorateCircleProperty =
        AvaloniaProperty.RegisterAttached<ButtonGlass, Ellipse, GlassOptics?>("DecorateCircle");
    private static readonly AttachedProperty<ButtonGlass?> CircleLensProperty =
        AvaloniaProperty.RegisterAttached<ButtonGlass, Ellipse, ButtonGlass?>("CircleLens");

    static ButtonGlass()
    {
        AffectsRender<ButtonGlass>(CornerRadiusProperty, SurfaceBrushProperty, IsEffectivelyEnabledProperty);
        DecorateProperty.Changed.AddClassHandler<Border>(OnDecorateChanged);
        DecorateCircleProperty.Changed.AddClassHandler<Ellipse>(OnDecorateCircleChanged);
    }

    private readonly GlassOptics _optics;
    private readonly Border? _surfaceOwner;
    private int _rendered;
    private int _opticsFailed;
    private readonly bool _completeFace;

    private ButtonGlass(GlassOptics optics, Border? surfaceOwner = null, bool completeFace = false)
    {
        _optics = optics;
        _surfaceOwner = surfaceOwner;
        _completeFace = completeFace;
        IsHitTestVisible = false;
        Focusable = false;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _optics.Background.PropertyChanged += OnPaletteChanged;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _optics.Background.PropertyChanged -= OnPaletteChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPaletteChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == SolidColorBrush.ColorProperty)
            InvalidateVisual();
    }

    internal static ButtonGlass ForBorder(Border surface, GlassOptics optics, bool completeFace = false)
    {
        var rim = new ButtonGlass(optics, surface, completeFace);
        rim.Bind(CornerRadiusProperty, new Binding(nameof(Border.CornerRadius)) { Source = surface });
        rim.Bind(SurfaceBrushProperty, new Binding(nameof(Border.Background)) { Source = surface });
        rim.Bind(MarginProperty, new Binding(nameof(Border.Padding)) { Source = surface, Converter = RimMargin });
        return rim;
    }

    internal bool HasRendered => Volatile.Read(ref _rendered) != 0 && Volatile.Read(ref _opticsFailed) == 0;

    internal static IEnumerable<Style> CreateStyles(GlassOptics optics)
    {
        yield return CreateButtonStyle(optics);
        var combo = new Style(selector => selector.OfType<ComboBox>());
        combo.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(999)));
        yield return combo;
        yield return DecorateBorder(selector => selector.OfType<ComboBox>().Template().OfType<Border>().Name("Background"), optics);
        yield return DecorateBorder(selector => selector.OfType<ComboBox>().Template().OfType<Border>().Name("PopupBorder"), optics);
        yield return DecorateBorder(selector => selector.OfType<CheckBox>().Template().OfType<Border>().Name("NormalRectangle"), optics);
        yield return DecorateBorder(selector => selector.OfType<ListBox>().Class("file-drop").Template().OfType<Border>().Name("border"), optics);
        yield return DecorateBorder(selector => selector.OfType<TextBox>().Class("settings-input").Template().OfType<Border>().Name("PART_BorderElement"), optics);
        yield return DecorateBorder(selector => selector.OfType<Border>().Class("glass-surface"), optics);
        yield return DecorateRadioCircle("OuterEllipse", optics);
        yield return DecorateRadioCircle("CheckOuterEllipse", optics);
    }

    private static Style DecorateBorder(Func<Selector?, Selector> selector, GlassOptics optics)
    {
        var style = new Style(selector);
        style.Setters.Add(new Setter(DecorateProperty, optics));
        style.Setters.Add(new Setter(Border.BoxShadowProperty, PillShadow));
        return style;
    }

    private static Style DecorateRadioCircle(string name, GlassOptics optics)
    {
        var style = new Style(selector => selector.OfType<RadioButton>().Template().OfType<Ellipse>().Name(name));
        style.Setters.Add(new Setter(DecorateCircleProperty, optics));
        return style;
    }

    private static void OnDecorateCircleChanged(Ellipse circle, AvaloniaPropertyChangedEventArgs _) =>
        Dispatcher.UIThread.Post(() => ApplyCircleLens(circle));

    private static void ApplyCircleLens(Ellipse circle)
    {
        if (circle.Parent is not Panel panel)
            return;
        if (circle.GetValue(CircleLensProperty) is { } previous)
            panel.Children.Remove(previous);
        circle.ClearValue(CircleLensProperty);
        if (circle.GetValue(DecorateCircleProperty) is not { } optics)
            return;
        var lens = new ButtonGlass(optics) { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        lens.SetValue(CornerRadiusProperty, new CornerRadius(999));
        lens.Bind(WidthProperty, new Binding(nameof(Ellipse.Width)) { Source = circle });
        lens.Bind(HeightProperty, new Binding(nameof(Ellipse.Height)) { Source = circle });
        lens.Bind(OpacityProperty, new Binding(nameof(Ellipse.Opacity)) { Source = circle });
        lens.Bind(SurfaceBrushProperty, new Binding(nameof(Ellipse.Fill)) { Source = circle });
        panel.Children.Insert(panel.Children.IndexOf(circle) + 1, lens);
        circle.SetValue(CircleLensProperty, lens);
    }

    private static void OnDecorateChanged(Border border, AvaloniaPropertyChangedEventArgs _)
    {
        if (border.Child is SurfaceLayers layers)
        {
            var content = layers.Children[1];
            layers.Children.Remove(content);
            border.Child = content;
        }
        else if (border.Child is ButtonGlass)
            border.Child = null;

        if (border.GetValue(DecorateProperty) is not { } optics)
            return;
        var rim = ForBorder(border, optics);
        if (border.Child is { } existing)
        {
            border.Child = null;
            border.Child = new SurfaceLayers { Children = { rim, existing } };
        }
        else
            border.Child = rim;
    }

    private sealed class SurfaceLayers : Grid { }

    internal static ButtonGlass? FindRim(Border border) => border.Child switch
    {
        ButtonGlass rim => rim,
        SurfaceLayers layers => (ButtonGlass)layers.Children[0],
        _ => null
    };

    private static LinearGradientBrush Gradient(string highlight, string neutral, string shadow) => new()
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops = new GradientStops
        {
            new(Color.Parse(highlight), 0),
            new(Color.Parse(neutral), .52),
            new(Color.Parse(shadow), 1)
        }
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
                {
                    presenter.SetValue(ContentPresenter.CornerRadiusProperty, new CornerRadius(999), BindingPriority.Template);
                    presenter.BoxShadow = PillShadow;
                }
                else
                    presenter.Bind(ContentPresenter.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new TemplateBinding(ContentControl.VerticalContentAlignmentProperty), BindingPriority.Template);
                if (button.Classes.Contains("start-glass"))
                {
                    var glass = new Border { CornerRadius = new CornerRadius(999), BoxShadow = PillShadow,
                        IsHitTestVisible = false };
                    glass.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                    glass.Bind(Border.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
                    glass.Bind(Border.BorderThicknessProperty, new TemplateBinding(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
                    glass.Child = ForBorder(glass, optics, completeFace: true);
                    // Fluent applies pointer and press colors to the named presenter.
                    // The lens owns the face, and the label follows the button's theme color.
                    presenter.Background = null;
                    presenter.BorderBrush = null;
                    presenter.Bind(ContentPresenter.ForegroundProperty,
                        new Binding(nameof(Button.Foreground)) { Source = button });
                    presenter.BoxShadow = default;
                    glass.Classes.Add("start-glass-motion");
                    presenter.Classes.Add("start-glass-motion");
                    // Keep the hit area stationary while the two visual parts move.
                    return new Grid { Background = Brushes.Transparent, Children = { glass, presenter } };
                }
                var rim = new ButtonGlass(optics);
                rim.Bind(CornerRadiusProperty, new Binding(nameof(ContentPresenter.CornerRadius)) { Source = presenter });
                rim.Bind(SurfaceBrushProperty, new Binding(nameof(ContentPresenter.Background)) { Source = presenter });
                return new Grid { Children = { presenter, rim } };
            })));
        return style;
    }

    internal static bool IsAppliedTo(Button button) =>
        button.GetVisualChildren().OfType<Grid>()
            .SelectMany(grid => grid.Children.Select(child => child is Border border ? FindRim(border) : child as ButtonGlass).OfType<ButtonGlass>()).Any(rim => rim.HasDrawableBounds);

    internal static bool HasRenderedPill(Button button) =>
        button.GetVisualChildren().OfType<Grid>()
            .SelectMany(grid => grid.Children.Select(child => child is Border border ? FindRim(border) : child as ButtonGlass).OfType<ButtonGlass>())
            .Any(rim => rim.HasRendered);

    private bool HasDrawableBounds => Bounds.Width >= 8 && Bounds.Height >= 8;

    public override void Render(DrawingContext context)
    {
        if (!HasDrawableBounds)
            return;

        var rect = new Rect(Bounds.Size);
        using var disabledFade = context.PushOpacity(IsEffectivelyEnabled ? 1 : .28);
        var radius = GetValue(CornerRadiusProperty);
        if (Volatile.Read(ref _opticsFailed) == 0)
        {
            var backdrop = ResolveBackdrop();
            var track = (GetValue(SurfaceBrushProperty) as ISolidColorBrush)?.Color ?? Colors.Transparent;
            var face = Blend(track, backdrop);
            // Static light surfaces can use the pill's complete face because their
            // content is drawn above the glass. Reuse its translucent track tint.
            bool fullFace = _completeFace || (_surfaceOwner is not null && track.A > 0 && face.R >= 220 &&
                            _optics.Background.Color.R >= 220);
            if (_completeFace)
                track = Color.FromArgb(242, face.R, face.G, face.B);
            else if (fullFace)
                track = Color.FromArgb(8, 0, 0, 0);
            // Keep the accent tint under the refracted rim so it does not form a solid ring.
            var trackSize = _completeFace ? new Size(rect.Width + 40, rect.Height + 40) : rect.Size;
            var origin = _completeFace ? new Vector(20, 20) : default;
            if (_optics.Draw(context, rect.Size, trackSize, origin, backdrop, track, face,
                    rimOnly: !fullFace, OnDraw, radius))
                return;
        }

        double limit = Math.Min(rect.Width, rect.Height) / 2;
        var outline = new RoundedRect(rect, new CornerRadius(
            Math.Min(radius.TopLeft, limit), Math.Min(radius.TopRight, limit),
            Math.Min(radius.BottomRight, limit), Math.Min(radius.BottomLeft, limit)));
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        context.DrawRectangle(dark ? DarkFace : LightFace, dark ? DarkEdge : LightEdge, outline.Deflate(.6, .6));
    }

    private Color ResolveBackdrop()
    {
        var color = _optics.Background.Color;
        foreach (var ancestor in this.GetVisualAncestors().Reverse())
        {
            if (ancestor == _surfaceOwner)
                continue;
            var brush = ancestor switch
            {
                Border border => border.Background,
                Panel panel => panel.Background,
                _ => null
            };
            if (brush is ISolidColorBrush solid)
                color = Blend(solid.Color, color);
        }
        return color;
    }

    private static Color Blend(Color front, Color back)
    {
        int alpha = front.A;
        byte Mix(byte first, byte second) => (byte)((first * alpha + second * (255 - alpha) + 127) / 255);
        return Color.FromRgb(Mix(front.R, back.R), Mix(front.G, back.G), Mix(front.B, back.B));
    }

    private void OnDraw(bool success)
    {
        if (success)
            Volatile.Write(ref _rendered, 1);
        else if (Interlocked.Exchange(ref _opticsFailed, 1) == 0)
            Dispatcher.UIThread.Post(InvalidateVisual);
    }
}
