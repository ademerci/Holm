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

        _singleInstance = new Mutex(true, Diag.DevFolder == null ? @"Local\Holm.SingleInstance" : @"Local\Holm.SingleInstance.Dev", out bool first);
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

#if DEBUG || BENCH
        // Dev helper: open and close the notch a number of times, write how smooth and costly that was, and quit.
        int bench = Array.IndexOf(e.Args, "--bench");
        if (bench >= 0 && bench + 1 < e.Args.Length)
        {
            AppSettings.ReadOnly = true; // your settings stay as they are
            int rounds = bench + 2 < e.Args.Length && int.TryParse(e.Args[bench + 2], out int n) ? n : 10;
            var window = new MainWindow();
            window.Loaded += async (_, _) => { await window.BenchAsync(e.Args[bench + 1], rounds); Shutdown(); };
            window.Show();
            return;
        }
#endif

        Diag.Enabled = e.Args.Contains("--diag");
        if (Diag.DevFolder == null) FormerName.Migrate(); // before the window reads the settings; test data leaves your autostart alone
        new MainWindow().Show();

#if DEBUG || BENCH
        // Measurement on test data: when the notch is ready (Loaded and the first frame done, the UI thread idle for the
        // first time) and 10 s later, write ms since process start, JIT'd methods and JIT time to HOLM_DATA\ready.txt.
        if (Diag.DevFolder is { } dev)
        {
            void Stamp(string label) => System.IO.File.AppendAllText(System.IO.Path.Combine(dev, "ready.txt"),
                $"{label} {(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} " +
                $"{System.Runtime.JitInfo.GetCompiledMethodCount()} {System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F0}{Environment.NewLine}");
            Dispatcher.InvokeAsync(() => Stamp("ready"), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var later = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            later.Tick += (_, _) => { later.Stop(); Stamp("10s"); };
            later.Start();
        }
#endif

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
