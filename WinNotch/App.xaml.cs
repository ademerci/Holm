using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace WinNotch;

public partial class App : Application
{
    Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            Diag.Error(ex.Exception);
            ex.Handled = true;
            // A crash before the notch is on screen would otherwise leave an invisible process behind.
            if (MainWindow is not { IsLoaded: true }) Shutdown(1);
        };

        _singleInstance = new Mutex(true, @"Local\WinNotch.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show(
                "WinNotch is already running. Only one instance can be open at a time.",
                "WinNotch is already running",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

#if DEBUG
        // Dev helper: render every notch state to PNGs and quit.
        int snap = Array.IndexOf(e.Args, "--snap");
        if (snap >= 0 && snap + 1 < e.Args.Length)
        {
            var window = new MainWindow();
            window.Loaded += async (_, _) => { await window.SnapshotAsync(e.Args[snap + 1]); Shutdown(); };
            window.Show();
            return;
        }
#endif

        Diag.Enabled = e.Args.Contains("--diag");
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
