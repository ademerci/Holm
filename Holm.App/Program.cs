using Avalonia;
using System;
using System.IO;

namespace Holm.App;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // One Holm at a time, the same way on every system: the first one holds a lock file, a second start finds it
        // taken and quits. The lock goes with the process, also when it crashes.
        // As in the WPF app: Holm --diag writes what it does to diag.log next to the settings.
        WinNotch.Diag.Enabled = Array.IndexOf(args, "--diag") >= 0;
        // Restarting (a new design): the old Holm is still letting go of the lock, so wait for it a few seconds.
        bool restart = Array.IndexOf(args, "--restart") >= 0;
        var instance = SingleInstance();
        for (int i = 0; instance == null && restart && i < 50; i++)
        {
            System.Threading.Thread.Sleep(200);
            instance = SingleInstance();
        }
        using var _ = instance;
        if (instance == null) return;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    static IDisposable? SingleInstance()
    {
        try
        {
            Directory.CreateDirectory(WinNotch.Diag.Folder);
            // Not the WPF app's mutex: on Windows the port runs next to an installed Holm.
            return new FileStream(Path.Combine(WinNotch.Diag.Folder, "holm-app.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; } // held by the running one
        catch (UnauthorizedAccessException) { return new MemoryStream(); } // can't tell: start rather than not at all
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
