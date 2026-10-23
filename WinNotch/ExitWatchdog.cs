using System;
using System.Diagnostics;
using System.Threading;

namespace WinNotch;

/// <summary>
/// Makes sure the process really ends once the app has quit. After an update, a quit Holm was once found still
/// running half an hour later with no window left, which also kept the updater waiting. Normally the process is
/// gone a second or two after quitting and this never fires; if it is still there after the grace period, the
/// watchdog notes it in error.log and ends the process.
/// </summary>
internal static class ExitWatchdog
{
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts the countdown on a background thread, so it never keeps the process alive itself.
    /// <paramref name="report"/> and <paramref name="end"/> replace writing error.log and ending the process (tests).
    /// </summary>
    public static Thread Arm(TimeSpan grace, Action<string>? report = null, Action? end = null)
    {
        var thread = new Thread(() =>
        {
            Thread.Sleep(grace);
            int threads = -1;
            try { using var self = Process.GetCurrentProcess(); threads = self.Threads.Count; } catch { }
            string message = $"Holm was still running {grace.TotalSeconds:0} s after quitting ({threads} threads); ending the process.";
            (report ?? (text => Diag.Error(new TimeoutException(text))))(message);
            (end ?? (() => Native.EndCurrentProcess(0)))();
        })
        { IsBackground = true, Name = "Holm exit watchdog" };
        thread.Start();
        return thread;
    }
}
