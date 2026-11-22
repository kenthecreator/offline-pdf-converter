using Avalonia;
using OfflinePDFConverter.Services;
using PdfSharp.Fonts;

namespace OfflinePDFConverter;

internal enum ModePillGlassTrial
{
    Off,
    On,
    SimulateFailure
}

internal static class Program
{
    internal static ModePillGlassTrial GlassTrial { get; private set; }
    internal static string? GuiVerificationReport { get; private set; }
    internal static int GuiVerificationExitCode { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        const string glassFlag = "--trial-liquid-glass";
        const string fallbackFlag = "--trial-liquid-glass=simulate-failure";
        if (args.Contains(fallbackFlag))
            GlassTrial = ModePillGlassTrial.SimulateFailure;
        else if (args.Contains(glassFlag))
            GlassTrial = ModePillGlassTrial.On;

        if (GlassTrial != ModePillGlassTrial.Off)
            args = args.Where(arg => arg != glassFlag && arg != fallbackFlag).ToArray();

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
        catch (Exception ex) when (GuiVerificationReport != null)
        {
            // Initialization may fail before a window exists. Preserve a failed report for CI.
            var report = Path.GetFullPath(GuiVerificationReport);
            Directory.CreateDirectory(Path.GetDirectoryName(report)!);
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(new
            {
                passed = false, stage = "desktop initialization or verification", checks = Array.Empty<string>(),
                error = ex.ToString(), operatingSystem = Environment.OSVersion.ToString()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Environment.ExitCode = 1;
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
