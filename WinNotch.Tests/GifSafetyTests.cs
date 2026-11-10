using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinNotch;

namespace WinNotch.Tests;

/// <summary>
/// GIFs come from anyone a friend links to, so a broken or hostile one must cost that GIF and never Holm: whatever
/// bytes arrive, decoding gives an animation or null, never an exception.
/// </summary>
public class GifSafetyTests
{
    /// <summary>A real animated GIF of three frames, made by Windows' own encoder.</summary>
    static byte[] SampleGif(int width = 40, int height = 30, int frames = 3)
    {
        var encoder = new GifBitmapEncoder();
        for (int i = 0; i < frames; i++)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Navy, null, new Rect(0, 0, width, height));
                dc.DrawEllipse(i % 2 == 0 ? Brushes.Gold : Brushes.Tomato, null, new Point(5 + i * 10, height / 2.0), 6, 6);
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
    public void A_good_gif_passes_the_check_whole_and_decodes()
    {
        byte[] gif = SampleGif();
        var result = GifInspector.Check(gif);
        Assert.NotNull(result);
        Assert.Equal((40, 30, 3), (result!.Width, result.Height, result.Frames));
        Assert.Same(gif, result.Data);
        var animation = GifDecoder.Decode(gif);
        Assert.NotNull(animation);
        Assert.Equal(3, animation!.Frames.Count);
    }

    [Fact]
    public void A_cut_off_gif_keeps_its_whole_frames_and_never_throws()
    {
        byte[] gif = SampleGif();
        for (int length = 0; length < gif.Length; length++)
        {
            byte[] cut = gif[..length];
            var result = GifInspector.Check(cut);
            if (result != null) Assert.Equal(0x3B, result.Data[^1]); // closed properly before Windows sees it
            _ = GifDecoder.Decode(cut);                               // throws nothing, whatever it gives
        }
    }

    [Fact]
    public void Garbage_after_the_frames_is_cut_away()
    {
        byte[] gif = SampleGif();
        byte[] withJunk = gif[..^1].Concat(new byte[] { 0x99, 0x01, 0x02, 0x03 }).ToArray(); // no trailer, junk instead
        var result = GifInspector.Check(withJunk);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Frames);
        Assert.Equal(0x3B, result.Data[^1]);
        Assert.NotNull(GifDecoder.Decode(withJunk));
    }

    [Fact]
    public void An_absurd_canvas_or_frame_is_turned_away()
    {
        byte[] huge = SampleGif();
        huge[6] = 0xFF; huge[7] = 0xFF; huge[8] = 0xFF; huge[9] = 0xFF; // 65535 x 65535
        Assert.Null(GifInspector.Check(huge));
        Assert.Null(GifDecoder.Decode(huge));

        byte[] frame = SampleGif();
        int at = Array.IndexOf(frame, (byte)0x2C, 13); // the first image descriptor
        frame[at + 5] = 0xFF; frame[at + 6] = 0xFF;    // a frame 65535 wide
        Assert.Null(GifInspector.Check(frame));
        Assert.Null(GifDecoder.Decode(frame));
    }

    [Fact]
    public void Not_a_gif_or_nearly_nothing_is_null()
    {
        Assert.Null(GifDecoder.Decode(Array.Empty<byte>()));
        Assert.Null(GifDecoder.Decode("GIF89a"u8.ToArray()));
        Assert.Null(GifDecoder.Decode("GIF89a\x01\x00\x01\x00\x00\x00\x00;"u8.ToArray()));
        Assert.Null(GifDecoder.Decode("\x89PNG\r\n\x1a\n"u8.ToArray()));
    }

    [Fact]
    public void Thousands_of_damaged_gifs_never_throw()
    {
        byte[] gif = SampleGif(64, 48, 4);
        var random = new Random(1234);
        for (int round = 0; round < 3000; round++)
        {
            byte[] bad = (byte[])gif.Clone();
            int changes = 1 + random.Next(8);
            for (int i = 0; i < changes; i++)
            {
                int at = 6 + random.Next(bad.Length - 6); // keep the signature, so it gets past the first check
                bad[at] = random.Next(4) switch { 0 => 0x00, 1 => 0xFF, 2 => (byte)(bad[at] ^ (1 << random.Next(8))), _ => (byte)random.Next(256) };
            }
            if (random.Next(5) == 0) bad = bad[..(6 + random.Next(bad.Length - 6))];
            var animation = GifDecoder.Decode(bad, 96);
            if (animation != null)
            {
                Assert.NotEmpty(animation.Frames);
                Assert.Equal(animation.Frames.Count, animation.Delays.Count);
                Assert.True(animation.Duration > TimeSpan.Zero);
                _ = GifPlayer.Animation(animation); // what the chat builds from it works too
            }
        }
    }

    [Fact]
    public void A_very_long_gif_is_cut_to_what_holm_will_draw()
    {
        byte[] gif = SampleGif(200, 200, 40);
        var result = GifInspector.Check(gif);
        Assert.NotNull(result);
        Assert.Equal(40, result!.Frames);
        var animation = GifDecoder.Decode(gif, 320);
        Assert.NotNull(animation);
        Assert.True(animation!.Bytes <= GifDecoder.MaxBytes);
    }
}
