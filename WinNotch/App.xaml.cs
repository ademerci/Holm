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

        _singleInstance = new Mutex(true, @"Local\Holm.SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show(
                "Holm is already running. Only one instance can be open at a time.",
                "Holm is already running",
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
            // Posing turns off animations and resets the sizes, and the friends inbox saves as it catches up:
            // without this, the pose ended up in your settings.
            AppSettings.ReadOnly = true;
            var window = new MainWindow();
            window.Loaded += async (_, _) => { await window.SnapshotAsync(e.Args[snap + 1]); Shutdown(); };
            window.Show();
            return;
        }
#endif

        Diag.Enabled = e.Args.Contains("--diag");
        FormerName.Migrate(); // before the window reads the settings
        new MainWindow().Show();

#if DEBUG
        // Dev helpers to check that the process really ends: quit after N seconds like the menu's Quit
        // (--quit-after N), and with --linger leave a thread behind that would keep it alive (the watchdog's job).
        int quit = Array.IndexOf(e.Args, "--quit-after");
        if (quit >= 0 && quit + 1 < e.Args.Length && int.TryParse(e.Args[quit + 1], out int seconds))
        {
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (_, _) => { timer.Stop(); Shutdown(); };
            timer.Start();
        }
        _linger = e.Args.Contains("--linger");
#endif
    }

#if DEBUG
    bool _linger;
#endif

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        ExitWatchdog.Arm(ExitWatchdog.Grace);
#if DEBUG
        if (_linger) new Thread(() => Thread.Sleep(Timeout.Infinite)) { Name = "Holm linger test" }.Start();
#endif
        base.OnExit(e);
    }
}
