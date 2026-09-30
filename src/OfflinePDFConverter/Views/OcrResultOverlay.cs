using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OfflinePDFConverter.Views;

/// <summary>Soft indirect lighting behind a completed preview card.</summary>
public sealed class OcrResultOverlay : Control
{
    public static readonly StyledProperty<double> PositionProperty =
        AvaloniaProperty.Register<OcrResultOverlay, double>(nameof(Position), -1);
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    static OcrResultOverlay() => AffectsRender<OcrResultOverlay>(PositionProperty);
    private static readonly Color[] Palette = {
        Color.Parse("#FF68D6"), Color.Parse("#AE55FF"), Color.Parse("#526CFF"), Color.Parse("#CD77FF") };

    private static Color Blend(double phase, byte alpha)
    {
        var value = (phase - Math.Floor(phase)) * Palette.Length;
        var index = (int)value; var fraction = value - index;
        var first = Palette[index]; var second = Palette[(index + 1) % Palette.Length];
        return Color.FromArgb(alpha, (byte)(first.R + (second.R - first.R) * fraction),
            (byte)(first.G + (second.G - first.G) * fraction), (byte)(first.B + (second.B - first.B) * fraction));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Position < 0 || Bounds.Width < 8 || Bounds.Height < 8) return;
        var phase = Position - Math.Floor(Position);
        var angle = phase * Math.PI * 2;
        var rect = new Rect(Bounds.Size).Deflate(1);
        // Three concealed light sources drift around the card. A narrow glow and a
        // wider falloff give the edge depth while keeping the page itself unobscured.
        var keyLight = new BoxShadow { OffsetX = Math.Cos(angle) * 5,
            OffsetY = Math.Sin(angle) * 4, Blur = 10, Spread = 2, Color = Blend(phase, 185) };
        var fillLight = new BoxShadow { OffsetX = Math.Cos(angle + 2.1) * 5,
            OffsetY = Math.Sin(angle + 2.1) * 4, Blur = 13, Spread = 2, Color = Blend(phase + .33, 145) };
        var ambientLight = new BoxShadow { OffsetX = Math.Cos(angle + 4.2) * 4,
            OffsetY = Math.Sin(angle + 4.2) * 3, Blur = 20, Spread = 2, Color = Blend(phase + .66, 105) };
        var depth = new BoxShadow { OffsetY = 2, Blur = 7, Color = Color.FromArgb(45, 0, 0, 0) };
        using (context.PushOpacity(.92 + .08 * Math.Sin(angle)))
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), null,
                rect, 12, 12, new BoxShadows(depth, new[] { keyLight, fillLight, ambientLight }));
    }
}
