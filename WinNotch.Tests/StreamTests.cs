using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class StreamTests
{
    /// <summary>Two ends of a local TCP connection, each as a SecureChannel, as the relay would join them.</summary>
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
    public async Task A_viewer_gets_new_pictures_and_hears_when_the_stream_ends()
    {
        var (streamer, viewer) = await PairAsync();
        var frames = new List<byte[]>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var watching = StreamProtocol.WatchAsync(viewer, f => { lock (frames) frames.Add(f); }, cts.Token);

        Assert.True(await StreamProtocol.AnswerAsync(streamer, cts.Token));
        await using var host = new StreamHost();
        host.Add("lena", "Lena", streamer);
        Assert.Equal(new[] { "Lena" }, host.Names);

        host.Publish(Picture(1));
        await Until(() => { lock (frames) return frames.Count == 1; });
        host.Publish(Picture(1)); // the same screen: nothing new goes
        await Task.Delay(200);
        host.Publish(Picture(2));
        await Until(() => { lock (frames) return frames.Count == 2; });
        lock (frames) Assert.Equal(Picture(2), frames[1]);

        await host.DisposeAsync();
        Assert.True(await watching); // ended, not lost
        viewer.Dispose();
    }

    /// <summary>Through the real transit relay, as two friends would: made-up pictures the size of a desktop frame.</summary>
    [Fact]
    public async Task A_stream_goes_through_the_relay_at_several_frames_a_second()
    {
        byte[] secret = ShareCode.NewSecret();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var streamerLink = RelayLink.ConnectWithRetryAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(secret), TimeSpan.FromSeconds(40), cts.Token);
        var viewerLink = RelayLink.ConnectWithRetryAsync(RelayLink.DefaultHost, RelayLink.DefaultPort, ShareCode.RelayToken(secret), TimeSpan.FromSeconds(40), cts.Token);
        var streamer = new SecureChannel(await streamerLink, ShareCode.Key(secret), initiator: true);
        var viewer = new SecureChannel(await viewerLink, ShareCode.Key(secret), initiator: false);

        int frames = 0;
        var watching = StreamProtocol.WatchAsync(viewer, _ => Interlocked.Increment(ref frames), cts.Token);
        Assert.True(await StreamProtocol.AnswerAsync(streamer, cts.Token));
        var host = new StreamHost();
        host.Add("lena", "Lena", streamer);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (byte i = 1; clock.Elapsed < TimeSpan.FromSeconds(5); i++)
        {
            host.Publish(Picture(i, 80_000));
            await Task.Delay(125);
        }
        double rate = frames / clock.Elapsed.TotalSeconds;
        await host.DisposeAsync();
        Assert.True(await watching);
        viewer.Dispose();
        Assert.True(rate >= 2, $"{rate:0.0} frames a second");
    }

    [Fact]
    public async Task Not_now_tells_the_streamer_straight_away()
    {
        var (streamer, viewer) = await PairAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await StreamProtocol.DeclineAsync(viewer, cts.Token);
        Assert.False(await StreamProtocol.AnswerAsync(streamer, cts.Token));
        streamer.Dispose();
        viewer.Dispose();
    }

    [Fact]
    public async Task A_viewer_who_closes_the_window_leaves_the_stream()
    {
        var (streamer, viewer) = await PairAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var closed = new CancellationTokenSource();
        var watching = StreamProtocol.WatchAsync(viewer, _ => { }, closed.Token);
        Assert.True(await StreamProtocol.AnswerAsync(streamer, cts.Token));
        await using var host = new StreamHost();
        int changes = 0;
        host.ViewersChanged += () => Interlocked.Increment(ref changes);
        host.Add("lena", "Lena", streamer);

        closed.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
        viewer.Dispose(); // the window is gone

        // The next pictures can't reach them: they're dropped from the stream.
        for (byte i = 3; i < 60 && host.Names.Count > 0; i++)
        {
            host.Publish(Picture(i, 200_000));
            await Task.Delay(50);
        }
        Assert.Empty(host.Names);
        Assert.True(changes >= 2);
    }

    [Fact]
    public async Task A_stream_that_goes_quiet_counts_as_gone()
    {
        var (streamer, viewer) = await PairAsync();
        // Nothing but the answer: the viewer waits StreamProtocol.Silence, so cancel sooner to keep the test short.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var watching = StreamProtocol.WatchAsync(viewer, _ => { }, cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watching);
        streamer.Dispose();
        viewer.Dispose();
    }

    [Fact]
    public void A_picture_larger_than_a_channel_message_is_refused()
    {
        Assert.Throws<ArgumentException>(() => StreamProtocol.FrameMessage(new byte[SecureChannel.MaxPlaintext]));
        Assert.Equal(StreamProtocol.Frame, StreamProtocol.FrameMessage(new byte[] { 1, 2 })[0]);
    }

    [Fact]
    public void A_stream_invitation_carries_its_secret_and_older_Holm_leaves_it_unopened()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        byte[] secret = ShareCode.NewSecret();
        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Stream, "Noah", 1, Secret: secret)), out _);
        Assert.Equal(FriendNoteKind.Stream, note!.Kind);
        Assert.Equal(secret, note.Secret);
        Assert.Null(FriendNotes.Open(lena, FriendNotes.SealKind(me, lena.AsFriend(), "stream-v2", "Noah", 1), out _));
    }

    [Fact]
    public void A_screen_picture_is_a_small_jpeg_or_nothing_when_there_is_no_desktop()
    {
        // A corner of the screen, kept in memory only.
        byte[]? jpeg = null;
        var thread = new Thread(() => jpeg = ScreenCapture.Jpeg(new System.Windows.Int32Rect(0, 0, 64, 48)));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (jpeg == null) return; // a session without a desktop
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, jpeg[..2]);
        Assert.InRange(jpeg.Length, 100, 100_000);
    }
}
