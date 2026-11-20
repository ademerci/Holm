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

        // Fluent paints checkbox labels separately for each state, including inside the template.
        foreach (string value in new[] { "Unchecked", "Checked", "Indeterminate" })
            foreach (string state in new[] { "", "PointerOver", "Pressed", "Disabled" })
                Resources["CheckBoxForeground" + value + state] = Look.Ink;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Menus and tooltips open in windows of their own: they draw text the same way as the notch.
        Avalonia.Controls.Control.LoadedEvent.AddClassHandler<Avalonia.Controls.Primitives.PopupRoot>(
            (popup, _) => Fonts.ApplyRendering(popup), handledEventsToo: true);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new NotchWindow(CreatePlatform());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The backend for the system the app was built for; the only place that knows which one that is.</summary>
#if !HOLM_WINDOWS
    static IPlatformServices CreateLinux() => new Holm.Platform.Linux.LinuxPlatformServices();
#endif

    static IPlatformServices CreatePlatform()
    {
#if HOLM_WINDOWS
        return new Holm.Platform.Windows.WindowsPlatformServices();
#else
        // Each backend sits behind a method of its own, so that its code is only touched on its system.
        if (System.OperatingSystem.IsLinux()) return CreateLinux();
        return new NoPlatformServices();
#endif
    }
}
