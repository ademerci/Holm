using Avalonia;
using System;

namespace Holm.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            // macOS: a notch is no app to switch to, so no Dock icon (Windows: ShowInTaskbar; Linux: the dock type).
            .With(new MacOSPlatformOptions { ShowInDock = false })
            .LogToTrace();
}
