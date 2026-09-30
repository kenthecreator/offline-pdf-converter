using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OfflinePDFConverter.Views;

/// <summary>A scanning beam drawn over OCR target page thumbnails.</summary>
public sealed class OcrScanOverlay : Control
{
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<OcrScanOverlay, double>(nameof(Position), -1);
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    static OcrScanOverlay() => AffectsRender<OcrScanOverlay>(PositionProperty);
    private static readonly IBrush Tint = new SolidColorBrush(Color.FromArgb(25, 40, 180, 255));
    private static readonly IPen Frame = new Pen(new SolidColorBrush(Color.FromArgb(180, 40, 180, 255)), 1);
    private static readonly IPen Beam = new Pen(new SolidColorBrush(Color.Parse("#33CFFF")), 2);
    private static readonly IBrush Trail = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(Color.FromArgb(0, 40, 180, 255), 0), new(Color.FromArgb(95, 40, 180, 255), 1) }
    };
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Position < 0 || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var rect = new Rect(Bounds.Size);
        var y = Math.Clamp(Position, 0, 1) * Math.Max(0, Bounds.Height - 2) + 1;
        context.DrawRectangle(Tint, Frame, rect.Deflate(.5));
        var height = Math.Min(Bounds.Height * .25, y);
        context.DrawRectangle(Trail, null, new Rect(0, y - height, Bounds.Width, height));
        context.DrawLine(Beam, new Point(0, y), new Point(Bounds.Width, y));
    }
}
