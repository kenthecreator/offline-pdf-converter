using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OfflinePDFConverter.Views;

/// <summary>A persistent completion mark, independent of the animated backlight.</summary>
public sealed class OcrCompletionBadge : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 28 || Bounds.Height < 28) return;
        var center = new Point(Bounds.Width - 14, 14);
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#23A65B")),
            new Pen(Brushes.White, 1), center, 9, 9);
        var check = new StreamGeometry();
        using (var path = check.Open())
        {
            path.BeginFigure(new Point(center.X - 4, center.Y), false);
            path.LineTo(new Point(center.X - 1, center.Y + 3));
            path.LineTo(new Point(center.X + 4, center.Y - 3));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(Brushes.White, 2, lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round), check);
    }
}
