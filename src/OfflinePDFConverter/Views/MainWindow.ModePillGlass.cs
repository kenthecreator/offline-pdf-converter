using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OfflinePDFConverter.Views;

public partial class MainWindow
{
    private static Border? CreateModePillGlass(Border pill, ModePillGlassTrial trial)
    {
        // The existing fill and outline stay visible if the decoration is unavailable.
        if (trial == ModePillGlassTrial.SimulateFailure)
            return null;

        var inset = pill.BorderThickness.Left;
        return new Border
        {
            CornerRadius = new CornerRadius(pill.CornerRadius.TopLeft - inset),
            IsHitTestVisible = false,
            Focusable = false,
            Background = new LinearGradientBrush
            {
                StartPoint = RelativePoint.TopLeft,
                EndPoint = RelativePoint.BottomRight,
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(58, 171, 219, 255), 0),
                    new(Colors.Transparent, 0.48),
                    new(Color.FromArgb(20, 30, 43, 62), 1)
                }
            },
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = RelativePoint.TopLeft,
                EndPoint = RelativePoint.BottomRight,
                GradientStops = new GradientStops
                {
                    new(Color.FromArgb(135, 255, 255, 255), 0),
                    new(Color.FromArgb(56, 219, 239, 255), 0.55),
                    new(Colors.Transparent, 1)
                }
            }
        };
    }
}
