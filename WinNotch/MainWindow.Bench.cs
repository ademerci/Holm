#if DEBUG || BENCH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch;

// Dev helper (Debug builds, or Release built with -p:HolmBench=true): opens and closes the notch a number of times and
// writes how smooth that was and what it cost to a text file: the longest gap between two frames while the shape
// moves (anything past ~17 ms at 60 Hz is a skipped frame), how long opening took before the first frame, the CPU time
// of all rounds, and memory. Only numbers are written, never what the notch shows.
public partial class MainWindow
{
    internal async Task BenchAsync(string file, int rounds)
    {
        var log = new StringBuilder();
        await Task.Delay(6000); // let the sources report in and start-up settle
        var proc = Process.GetCurrentProcess();

        var frames = new List<double>();
        TimeSpan last = TimeSpan.Zero;
        void OnFrame(object? sender, EventArgs e)
        {
            var t = ((RenderingEventArgs)e).RenderingTime;
            if (t == last) return; // fires more than once for one frame
            if (last != TimeSpan.Zero) frames.Add((t - last).TotalMilliseconds);
            last = t;
        }

        // What opening costs, step by step (the steps SetExpanded takes), once before the rounds.
        log.AppendLine(TimeOpenSteps());
        SetExpanded(false);
        await Task.Delay(1500);

        var opens = new List<(double Call, double Max, int Skipped)>();
        var uiOpen = new List<double>();
        var uiClose = new List<double>();
        var closes = new List<(double Call, double Max, int Skipped)>();
        proc.Refresh();
        var cpu0 = proc.TotalProcessorTime;
        for (int i = 0; i < rounds; i++)
        {
            foreach (bool open in new[] { true, false })
            {
                frames.Clear();
                last = TimeSpan.Zero;
                CompositionTarget.Rendering += OnFrame;
                long ui0 = ThreadCpu();
                var watch = Stopwatch.StartNew();
                SetExpanded(open);
                double call = watch.Elapsed.TotalMilliseconds;
                await Task.Delay(650);
                CompositionTarget.Rendering -= OnFrame;
                (open ? uiOpen : uiClose).Add((ThreadCpu() - ui0) / 10_000.0);
                double max = frames.Count > 0 ? frames.Max() : 0;
                int skipped = frames.Sum(f => (int)Math.Max(0, Math.Round(f / 16.7) - 1));
                (open ? opens : closes).Add((call, max, skipped));
                await Task.Delay(600);
            }
        }
        proc.Refresh();
        var cpu = proc.TotalProcessorTime - cpu0;

        string Row(string name, List<(double Call, double Max, int Skipped)> r)
            => $"{name}: call median {Median(r.Select(x => x.Call)):F1} ms (max {r.Max(x => x.Call):F1}), "
             + $"longest frame median {Median(r.Select(x => x.Max)):F1} ms (max {r.Max(x => x.Max):F1}), skipped frames {r.Sum(x => x.Skipped)}";
        log.AppendLine(Row("open ", opens));
        log.AppendLine(Row("close", closes));
        log.AppendLine($"cpu {cpu.TotalMilliseconds:F0} ms for {rounds} open+close rounds ({cpu.TotalMilliseconds / rounds:F0} ms each); "
                     + $"the notch's thread: open median {Median(uiOpen):F0} ms, close median {Median(uiClose):F0} ms");
        if (Environment.GetEnvironmentVariable("HOLM_BENCH_QUICK") == "1") { File.WriteAllText(file, log.ToString()); return; }

        // A quiet minute closed: what runs on the notch's thread, and what runs elsewhere.
        await Task.Delay(8000);
        var work = new Dictionary<string, (int Count, double Ms)>();
        var started = new Dictionary<DispatcherOperation, long>();
        void Started(object? sender, DispatcherHookEventArgs e) => started[e.Operation] = Stopwatch.GetTimestamp();
        void Ended(object? sender, DispatcherHookEventArgs e)
        {
            if (!started.Remove(e.Operation, out long at)) return;
            string name = Describe(e.Operation);
            var (count, ms) = work.GetValueOrDefault(name);
            work[name] = (count + 1, ms + Stopwatch.GetElapsedTime(at).TotalMilliseconds);
        }
        Dispatcher.Hooks.OperationStarted += Started;
        Dispatcher.Hooks.OperationCompleted += Ended;
        Dispatcher.Hooks.OperationAborted += Ended;
        proc.Refresh();
        var idle0 = proc.TotalProcessorTime;
        long ui0 = ThreadCpu();
        await Task.Delay(60_000);
        proc.Refresh();
        Dispatcher.Hooks.OperationStarted -= Started;
        Dispatcher.Hooks.OperationCompleted -= Ended;
        Dispatcher.Hooks.OperationAborted -= Ended;
        double uiMs = (ThreadCpu() - ui0) / 10_000.0;
        log.AppendLine($"idle cpu {(proc.TotalProcessorTime - idle0).TotalMilliseconds:F0} ms/min, of that the notch's thread {uiMs:F0} ms");
        foreach (var (name, (count, ms)) in work.OrderByDescending(w => w.Value.Ms).Take(12))
            log.AppendLine($"  {ms,7:F1} ms  {count,4}x  {name}");
        var gc = GC.GetGCMemoryInfo();
        log.AppendLine($"private {proc.PrivateMemorySize64 / 1048576.0:F1} MB, working set {proc.WorkingSet64 / 1048576.0:F1} MB, "
                     + $"gc heap {gc.HeapSizeBytes / 1048576.0:F1} MB, gc committed {gc.TotalCommittedBytes / 1048576.0:F1} MB");

        // The Holm window opened and closed a few times: does anything of it stay behind?
        long heap0 = Collected();
        proc.Refresh();
        long private0 = proc.PrivateMemorySize64;
        var windows = new List<WeakReference>();
        for (int i = 0; i < 5; i++)
        {
            ShowHub();
            windows.Add(new WeakReference(_settingsWindow));
            await Task.Delay(1800);
            _settingsWindow?.Close();
            await Task.Delay(800);
        }
        await Task.Delay(2000);
        long heap1 = Collected();
        proc.Refresh();
        log.AppendLine($"holm window 5x: {windows.Count(w => w.IsAlive)} of 5 still alive, gc heap {(heap1 - heap0) / 1048576.0:+0.0;-0.0} MB, "
                     + $"private {(proc.PrivateMemorySize64 - private0) / 1048576.0:+0.0;-0.0} MB");
        gc = GC.GetGCMemoryInfo();
        log.AppendLine($"private {proc.PrivateMemorySize64 / 1048576.0:F1} MB, working set {proc.WorkingSet64 / 1048576.0:F1} MB, "
                     + $"gc heap {gc.HeapSizeBytes / 1048576.0:F1} MB, gc committed {gc.TotalCommittedBytes / 1048576.0:F1} MB");
        File.WriteAllText(file, log.ToString());
    }

    string TimeOpenSteps()
    {
        var line = new StringBuilder("open steps (ms):");
        void Time(string name, Action step)
        {
            var watch = Stopwatch.StartNew();
            step();
            line.Append($" {name} {watch.Elapsed.TotalMilliseconds:F1}");
        }
        _expanded = true;
        Time("media", RefreshMediaUi);
        Time("devices", RefreshDevices);
        Time("output", RefreshOutput);
        Time("bluetooth", RefreshBluetooth);
        Time("timer", ShowTimer);
        Time("focumon", RefreshFocumon);
        Time("ai", () => ResolveAi(force: false));
        Time("state", () => ApplyState());
        Time("backdrop", UpdateBackdrop);
        Time("privacy", ShowPrivacy);
        Time("shelf", ShowShelf);
        Time("layout", UpdateLayout);
        return line.ToString();
    }

    static readonly FieldInfo? OperationMethod = typeof(DispatcherOperation).GetField("_method", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo? OperationArgs = typeof(DispatcherOperation).GetField("_args", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo? TimerTick = typeof(DispatcherTimer).GetField("Tick", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>What a piece of work on the notch's thread is: a timer by what it calls, otherwise the method.</summary>
    static string Describe(DispatcherOperation operation)
    {
        if (OperationMethod?.GetValue(operation) is not Delegate method) return operation.Priority.ToString();
        var timer = method.Target as DispatcherTimer ?? OperationArgs?.GetValue(operation) as DispatcherTimer;
        if (timer != null && TimerTick?.GetValue(timer) is EventHandler tick)
            return $"timer {timer.Interval.TotalMilliseconds:F0} ms: " + string.Join(" + ", tick.GetInvocationList().Select(DelegateName));
        return DelegateName(method);
    }

    static string DelegateName(Delegate method)
    {
        string own = $"{method.Target?.GetType().Name ?? method.Method.DeclaringType?.Name}.{method.Method.Name}";
        // A lambda (as in MakeTimer) is named after the delegate it calls.
        var inner = method.Target?.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(f => f.GetValue(method.Target)).OfType<Delegate>().FirstOrDefault(d => d != method);
        return inner != null ? $"{own} -> {inner.Method.DeclaringType?.Name}.{inner.Method.Name}" : own;
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);

    /// <summary>CPU time of the calling thread, in 100 ns units.</summary>
    static long ThreadCpu() => GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user) ? kernel + user : 0;

    static long Collected()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        return GC.GetTotalMemory(true);
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
#endif
