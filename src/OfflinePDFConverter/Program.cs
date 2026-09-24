using Avalonia;
using OfflinePDFConverter.Services;
using PdfSharp.Fonts;

namespace OfflinePDFConverter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-offline")
        {
            Environment.ExitCode = OfflineSelfTest.RunAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        GlobalFontSettings.FontResolver ??= new AppFontResolver();
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { BundledOcrRuntime.Cleanup(); }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
