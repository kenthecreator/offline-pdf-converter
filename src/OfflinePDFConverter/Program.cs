using Avalonia;
using OfflinePDFConverter.Services;
using PdfSharp.Fonts;

namespace OfflinePDFConverter;

internal static class Program
{
    internal static string? GuiVerificationReport { get; private set; }
    internal static int GuiVerificationExitCode { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-offline")
        {
            Environment.ExitCode = OfflineSelfTest.RunAsync(args[1]).GetAwaiter().GetResult();
            return;
        }
        if (args.Length == 2 && args[0] == "--verify-gui") GuiVerificationReport = args[1];
        GlobalFontSettings.FontResolver ??= new AppFontResolver();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            if (GuiVerificationReport != null) Environment.ExitCode = GuiVerificationExitCode;
        }
        finally
        {
#if PADDLE_OCR
            PaddleOcrService.Cleanup();
#endif
            BundledOcrRuntime.Cleanup();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}
