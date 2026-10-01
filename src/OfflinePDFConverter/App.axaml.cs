using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using OfflinePDFConverter.Views;

namespace OfflinePDFConverter;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var isSystemDarkTheme = PlatformSettings?.GetColorValues().ThemeVariant
                                    == PlatformThemeVariant.Dark;
            RequestedThemeVariant = isSystemDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            var window = new MainWindow(isSystemDarkTheme);
            desktop.MainWindow = window;
            if (Program.GuiVerificationReport is { } report)
            {
                // Keep the dispatcher alive until the verification report has been written after closing.
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
                window.Opened += async (_, _) =>
                {
                    Program.GuiVerificationExitCode = await window.VerifyGuiAsync(report);
                    window.Close();
                    desktop.Shutdown(Program.GuiVerificationExitCode);
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
