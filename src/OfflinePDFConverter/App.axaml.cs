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
            desktop.MainWindow = new MainWindow(isSystemDarkTheme);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
