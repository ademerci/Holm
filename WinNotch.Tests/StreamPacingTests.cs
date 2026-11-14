using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

/// <summary>
/// A stream keeps one frame on its way per viewer: the next goes once the viewer says it has shown the last, so a slow
/// connection gets fewer, fresher frames instead of seconds of old ones; and Holm on either side without receipts still works.
/// </summary>
public class StreamPacingTests
{
    static async Task<(SecureChannel Streamer, SecureChannel Viewer)> PairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var server = await listener.AcceptTcpClientAsync();
        listener.Stop();
        byte[] key = RandomNumberGenerator.GetBytes(32);
        return (new SecureChannel(server.GetStream(), key, initiator: true), new SecureChannel(client.GetStream(), key, initiator: false));
    }

    static byte[] Picture(byte fill, int size = 5000)
    {
        var jpeg = new byte[size];
        Array.Fill(jpeg, fill);
        jpeg[0] = 0xFF; jpeg[1] = 0xD8;
        return jpeg;
    }

    static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task The_next_frame_waits_for_the_viewers_receipt_and_a_viewer_that_was_busy_still_gets_the_change()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var host = new StreamHost();
        var (fastStreamer, fastViewer) = await PairAsync();
        var (slowStreamer, slowViewer) = await PairAsync();
        var fast = new List<byte>();
        var slow = new List<byte>();
        using var gate = new ManualResetEventSlim(false);
        var watchingFast = StreamProtocol.WatchAsync(fastViewer, f => { lock (fast) fast.Add(f[5]); }, cts.Token);
        var watchingSlow = StreamProtocol.WatchAsync(slowViewer, f => { lock (slow) slow.Add(f[5]); gate.Wait(cts.Token); }, cts.Token);
        foreach (var (key, streamer) in new[] { ("fast", fastStreamer), ("slow", slowStreamer) })
        {
            var answer = await StreamProtocol.AnswerAsync(streamer, cts.Token);
            Assert.True(answer.Watch && answer.Receipts);
            host.Add(key, key, streamer, answer.Receipts);
        }
        // As the capture loop does, again and again: a viewer still being asked for receipts gets it on a later round.
        await Until(() => { host.Publish(Picture(1)); lock (fast) lock (slow) return fast.Count == 1 && slow.Count == 1; });
        await Until(() => host.SlowestRoundTripMs > 0); // the fast one's receipt is in

        host.Publish(Picture(2)); // the slow viewer is still showing 1: only the fast one gets 2
        await Until(() => { lock (fast) return fast.Count == 2; });
        await Task.Delay(150);
        lock (slow) Assert.Single(slow);

        gate.Set(); // the slow viewer shows its frame and sends the receipt
        await Until(() => host.AnyReady);
        await Task.Delay(100);
        host.Publish(Picture(2)); // nothing new for the fast one, but the slow one never saw 2
        await Until(() => { lock (slow) return slow.Count == 2; });
        lock (slow) Assert.Equal(new byte[] { 1, 2 }, slow);
        lock (fast) Assert.Equal(new byte[] { 1, 2 }, fast);

        await host.DisposeAsync();
        Assert.True(await watchingFast);
        Assert.True(await watchingSlow);
    }

    [Fact]
    public async Task A_viewer_on_older_holm_sends_no_receipts_and_still_gets_every_change()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var (streamer, viewer) = await PairAsync();
        await viewer.SendAsync(new[] { StreamProtocol.Watch }, cts.Token); // what Holm 1.11 says
        var answer = await StreamProtocol.AnswerAsync(streamer, cts.Token);
        Assert.True(answer.Watch);
        Assert.False(answer.Receipts);
        await using var host = new StreamHost();
        host.Add("old", "Old", streamer, answer.Receipts);

        host.Publish(Picture(1));
        Assert.Equal(StreamProtocol.Frame, (await viewer.ReceiveAsync(cts.Token))[0]);
        await Until(() => host.AnyReady); // free again once sent, without a receipt
        host.Publish(Picture(2));
        var second = await viewer.ReceiveAsync(cts.Token);
        Assert.Equal(StreamProtocol.Frame, second[0]);
        Assert.Equal(2, second[5]);
        viewer.Dispose();
    }

    [Theory]
    [InlineData(60, 900, 50)]
    [InlineData(60, 500, 56)]
    [InlineData(60, 300, 60)]
    [InlineData(60, 100, 63)]
    [InlineData(StreamProtocol.MinQuality, 5000, StreamProtocol.MinQuality)]
    [InlineData(StreamProtocol.MaxQuality, 0, StreamProtocol.MaxQuality)]
    public void Quality_follows_how_long_frames_take(int quality, double roundTripMs, int next)
        => Assert.Equal(next, StreamProtocol.NextQuality(quality, roundTripMs));

    [Theory]
    [InlineData(3840, 2160, 1280, 720)]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1080, 1920, 720, 1280)] // a portrait screen is held to its height too
    [InlineData(1024, 768, 1024, 768)]
    public void A_screen_is_sent_within_1280_pixels_in_its_own_shape(int width, int height, int sentWidth, int sentHeight)
        => Assert.Equal((sentWidth, sentHeight), StreamProtocol.FitWithin(width, height, ScreenCapture.MaxSide));
}
