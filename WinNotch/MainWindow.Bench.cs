#if DEBUG || BENCH
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

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

        var opens = new List<(double Call, double Max, int Skipped)>();
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
                var watch = Stopwatch.StartNew();
                SetExpanded(open);
                double call = watch.Elapsed.TotalMilliseconds;
                await Task.Delay(650);
                CompositionTarget.Rendering -= OnFrame;
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
        log.AppendLine($"cpu {cpu.TotalMilliseconds:F0} ms for {rounds} open+close rounds ({cpu.TotalMilliseconds / rounds:F0} ms each)");

        // A quiet half minute closed.
        await Task.Delay(8000);
        proc.Refresh();
        var idle0 = proc.TotalProcessorTime;
        await Task.Delay(30_000);
        proc.Refresh();
        log.AppendLine($"idle cpu {(proc.TotalProcessorTime - idle0).TotalMilliseconds * 2:F0} ms/min");
        var gc = GC.GetGCMemoryInfo();
        log.AppendLine($"private {proc.PrivateMemorySize64 / 1048576.0:F1} MB, working set {proc.WorkingSet64 / 1048576.0:F1} MB, "
                     + $"gc heap {gc.HeapSizeBytes / 1048576.0:F1} MB, gc committed {gc.TotalCommittedBytes / 1048576.0:F1} MB");
        File.WriteAllText(file, log.ToString());
    }

    static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
#endif
