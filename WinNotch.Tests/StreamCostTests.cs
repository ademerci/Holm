using System.Diagnostics;
using WinNotch;
using WinNotch.Sharing;
using Xunit.Abstractions;

namespace WinNotch.Tests;

/// <summary>What one streamed picture of this PC's main screen costs: bytes on the wire, time and memory to make it.</summary>
[Trait("Category", "Local")]
public class StreamCostTests(ITestOutputHelper output)
{
    [Fact]
    public void A_streamed_picture_of_the_screen_is_smaller_than_before()
    {
        var screen = Native.Screens().First();
        var area = new System.Windows.Int32Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Right - screen.Bounds.Left, screen.Bounds.Bottom - screen.Bounds.Top);
        (long Bytes, double Ms, long Allocated) Measure(int side, int quality)
        {
            (long, double, long) result = default;
            var thread = new Thread(() =>
            {
                using var grabber = new ScreenCapture.Grabber(area, side);
                grabber.Jpeg(quality); // warm up
                long before = GC.GetAllocatedBytesForCurrentThread();
                var watch = Stopwatch.StartNew();
                long bytes = 0;
                for (int i = 0; i < 10; i++) bytes += grabber.Jpeg(quality)?.Length ?? 0;
                result = (bytes / 10, watch.Elapsed.TotalMilliseconds / 10, (GC.GetAllocatedBytesForCurrentThread() - before) / 10);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return result;
        }
        var old = Measure(1600, 70);
        var now = Measure(ScreenCapture.MaxSide, StreamProtocol.StartQuality);
        output.WriteLine($"{area.Width}×{area.Height}: 1600 px q70 {old.Bytes / 1024} KB, {old.Ms:F0} ms; 1280 px q60 {now.Bytes / 1024} KB, {now.Ms:F0} ms, {now.Allocated / 1024} KB allocated a frame");
        if (old.Bytes == 0) return; // no desktop
        Assert.True(now.Bytes < old.Bytes);
    }
}
