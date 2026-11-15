using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Holm.Platform;

namespace Holm.App;

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
            desktop.MainWindow = new NotchWindow(CreatePlatform());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The backend for the system the app was built for; the only place that knows which one that is.</summary>
    static IPlatformServices CreatePlatform()
    {
#if HOLM_WINDOWS
        return new Holm.Platform.Windows.WindowsPlatformServices();
#else
        return new NoPlatformServices();
#endif
    }
}
