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
    internal static ModePillGlassTrial GlassTrial { get; private set; } = ModePillGlassTrial.On;
    internal static string? GuiVerificationReport { get; private set; }
    internal static int GuiVerificationExitCode { get; set; }

    internal static void TraceGuiVerification(string message)
    {
        if (GuiVerificationReport is { } report)
            File.AppendAllText(Path.ChangeExtension(report, ".trace.txt"), $"{DateTime.UtcNow:O} {message}\n");
    }

    [STAThread]
    public static void Main(string[] args)
    {
        const string glassFlag = "--trial-liquid-glass";
        const string offFlag = "--trial-liquid-glass=off";
        const string fallbackFlag = "--trial-liquid-glass=simulate-failure";
        const string refractionFlag = "--trial-live-refraction";
        if (args.Contains(fallbackFlag))
            GlassTrial = ModePillGlassTrial.SimulateFailure;
        else if (args.Contains(offFlag))
            GlassTrial = ModePillGlassTrial.Off;
        else if (args.Contains(glassFlag))
            GlassTrial = ModePillGlassTrial.On;

        args = args.Where(arg => arg != glassFlag && arg != offFlag && arg != fallbackFlag && arg != refractionFlag).ToArray();

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
            TraceGuiVerification("Desktop lifetime ended");
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
