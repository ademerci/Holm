using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch;
using Xunit.Abstractions;

namespace WinNotch.Tests;

/// <summary>What a GIF costs: the frames it keeps, the memory it allocates on the way, and a still one decodes one frame.</summary>
public class GifCostTests(ITestOutputHelper output)
{
    static byte[] Gif(int width, int height, int frames)
    {
        var encoder = new GifBitmapEncoder();
        for (int i = 0; i < frames; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Navy, null, new Rect(0, 0, width, height));
                dc.DrawEllipse(Brushes.Gold, null, new Point(5 + i * 4 % width, height / 2.0), 12, 12);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
        }
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void A_long_gif_keeps_every_second_frame_at_bubble_size_and_its_whole_running_time()
    {
        var gif = GifDecoder.Decode(Gif(480, 270, 100), 320)!;
        Assert.Equal(320, gif.Width);
        Assert.Equal(180, gif.Height);
        Assert.Equal(51, gif.Frames.Count); // 16 MB hold 72 frames of 320 × 180: every second one, and the last
        Assert.Equal(TimeSpan.FromSeconds(10), gif.Duration);
        Assert.True(gif.Bytes <= GifDecoder.MaxBytes);
    }

    [Fact]
    public void A_gif_that_wont_move_decodes_only_its_first_frame()
    {
        var gif = GifDecoder.Decode(Gif(120, 80, 30), 320, maxFrames: 1)!;
        Assert.Single(gif.Frames);
    }

    [Fact]
    public void Decoding_a_long_gif_allocates_little_more_than_the_frames_it_keeps()
    {
        byte[] data = Gif(480, 270, 100);
        GifDecoder.Decode(data, 320); // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var gif = GifDecoder.Decode(data, 320)!;
        watch.Stop();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"480×270, 100 frames: {allocated / 1048576.0:F1} MB allocated, {gif.Bytes / 1048576.0:F1} MB kept, {watch.ElapsedMilliseconds} ms");
        // Before 1.12: a new buffer per frame and a full-size copy per kept frame, 50.8 MB for this GIF (12.9 MB after).
        Assert.True(allocated < 3 * gif.Bytes, $"{allocated} bytes allocated for {gif.Bytes} kept");
    }

    [Fact]
    public void Shrinking_averages_pixels_and_clear_ones_add_nothing()
    {
        byte[] canvas = { 255, 0, 0, 255, 0, 0, 0, 0, 0, 255, 0, 255, 0, 255, 0, 255 }; // 2 × 2
        Assert.Equal(new byte[] { 64, 128, 0, 191 }, GifDecoder.Shrink(canvas, 2, 2, 1, 1));
        Assert.Equal(new byte[] { 128, 128, 0, 255, 0, 128, 0, 128 }, GifDecoder.Shrink(canvas, 2, 2, 2, 1));
    }

    [Fact]
    public void A_frame_is_drawn_at_its_place_and_its_clear_pixels_leave_the_canvas_as_it_was()
    {
        var canvas = new byte[3 * 2 * 4];
        Array.Fill(canvas, (byte)9);
        byte[] frame = { 1, 2, 3, 255, 0, 0, 0, 0 }; // 2 × 1: one seen pixel, one clear
        GifDecoder.Draw(canvas, 12, 3, 2, frame, 2, 1, 2, 1); // at (2, 1): its second pixel falls off the canvas
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, canvas[20..24]);
        Assert.All(canvas[..20], b => Assert.Equal(9, b));
    }
}
