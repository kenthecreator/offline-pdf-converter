using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace OfflinePDFConverter.Views;

// Preserve the Fluent button presenter contract and add the optical rim in its local coordinates.
internal sealed class PillButtonGlass : Control
{
    private static readonly Pen Reflection = new(new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops = new GradientStops
        {
            new(Color.FromArgb(177, 255, 255, 255), 0),
            new(Color.FromArgb(32, 255, 255, 255), .48),
            new(Color.FromArgb(90, 255, 255, 255), 1)
        }
    }, 1.25);

    private static readonly Pen InnerShadow = new(new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = RelativePoint.BottomRight,
        GradientStops = new GradientStops
        {
            new(Color.FromArgb(29, 0, 0, 0), 0),
            new(Color.FromArgb(15, 0, 0, 0), .48),
            new(Color.FromArgb(105, 0, 0, 0), 1)
        }
    }, 1.15);

    private static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        TemplatedControl.CornerRadiusProperty.AddOwner<PillButtonGlass>();

    static PillButtonGlass() => AffectsRender<PillButtonGlass>(CornerRadiusProperty, IsEffectivelyEnabledProperty);

    internal static Style CreateStyle()
    {
        var style = new Style(selector => selector.OfType<Button>());
        style.Setters.Add(new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<Button>((_, scope) =>
            {
                var presenter = new ContentPresenter { Name = "PART_ContentPresenter" };
                scope.Register("PART_ContentPresenter", presenter);
                presenter.Bind(ContentPresenter.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.BorderThicknessProperty, new TemplateBinding(TemplatedControl.BorderThicknessProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(ContentControl.ContentTemplateProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.PaddingProperty, new TemplateBinding(TemplatedControl.PaddingProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, new TemplateBinding(ContentControl.HorizontalContentAlignmentProperty), BindingPriority.Template);
                presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, new TemplateBinding(ContentControl.VerticalContentAlignmentProperty), BindingPriority.Template);
                var rim = new PillButtonGlass { IsHitTestVisible = false, Focusable = false };
                rim.Bind(CornerRadiusProperty, new TemplateBinding(TemplatedControl.CornerRadiusProperty), BindingPriority.Template);
                return new Grid { Children = { presenter, rim } };
            })));
        return style;
    }

    internal static bool IsAppliedTo(Button button) =>
        button.GetVisualChildren().OfType<Grid>()
            .SelectMany(grid => grid.Children.OfType<PillButtonGlass>()).Any(rim => rim.IsPill);

    private bool IsPill
    {
        get
        {
            var bounds = Bounds;
            var corners = GetValue(CornerRadiusProperty);
            var radius = corners.TopLeft;
            return bounds.Height >= 8 && bounds.Width >= bounds.Height + 1 &&
                radius >= bounds.Height / 2 - 1 && radius == corners.TopRight &&
                radius == corners.BottomRight && radius == corners.BottomLeft;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (!IsPill)
            return;

        var rect = new Rect(Bounds.Size);
        using var disabledFade = context.PushOpacity(IsEffectivelyEnabled ? 1 : .28);
        DrawRim(context, rect.Deflate(1.3), Reflection);
        DrawRim(context, rect.Deflate(2.55), InnerShadow);
    }

    private static void DrawRim(DrawingContext context, Rect rect, Pen pen) =>
        context.DrawRectangle(null, pen, rect, rect.Height / 2, rect.Height / 2);
}
