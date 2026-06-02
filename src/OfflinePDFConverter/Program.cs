using Avalonia;
using OfflinePDFConverter.Services;
using PdfSharp.Fonts;

namespace OfflinePDFConverter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        GlobalFontSettings.FontResolver ??= new AppFontResolver();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
