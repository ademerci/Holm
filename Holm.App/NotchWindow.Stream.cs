using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Holm.Platform;
using SkiaSharp;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Streaming your screen to friends, like going live in a chat app (the WPF app's MainWindow.Stream.cs): they get an
/// invitation in their notch with Watch, and a window with your screen. While you're live a red badge sits under the
/// notch with Stop. On Windows the notch, the badge and the Holm window stay out of the picture; X11 can't keep a window
/// out of a picture, so there they're blacked out in it instead, and your messages and chats don't go along either way.
/// </summary>
public sealed partial class NotchWindow
{
    const int FrameMs = 125; // up to 8 pictures a second; a slow connection gets fewer
    /// <summary>The longer side of a streamed picture, as in the WPF app: text stays readable, a public relay copes.</summary>
    const int StreamMaxSide = 1280;

    StreamHost? _stream;
    CancellationTokenSource? _captureStop;
    StreamBadge? _badge;
    DateTime _streamStarted;
    readonly HashSet<string> _streamInvites = new();
    (Friend Friend, byte[] Secret)? _streamOffer;
    DispatcherTimer? _offerTimeout;
    /// <summary>Where Holm's own windows are in the streamed area, in its pixels, where the system can't hide them; read by the capture thread.</summary>
    volatile PixelRect[] _streamMask = Array.Empty<PixelRect>();
    bool _hiddenFromCapture;
    /// <summary>Where the system can't leave Holm out, follows its windows (the notch grows and shrinks) while live.</summary>
    DispatcherTimer? _maskTick;

    void InitStreaming()
    {
        _friends.StreamOffered += OnStreamOffered;
        if (Environment.GetEnvironmentVariable("HOLM_DEV_STREAM") == "1")
            Dispatcher.UIThread.Post(() => _ = StreamToSelfAsync(), DispatcherPriority.Background);
    }

    /// <summary>
    /// A debug aid (HOLM_DEV_STREAM=1): streams this screen to a viewer window of this same Holm over a loopback
    /// connection instead of the relay, through the same protocol, encryption, capture and windows as a friend's stream.
    /// </summary>
    async Task StreamToSelfAsync()
    {
        Diag.Enabled = true;
        await Task.Delay(2000);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var accept = listener.AcceptTcpClientAsync();
        var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        var served = await accept;
        listener.Stop();
        byte[] key = ShareCode.Key(ShareCode.NewSecret());
        var hostChannel = new SecureChannel(served.GetStream(), key, initiator: true);
        var viewerChannel = new SecureChannel(client.GetStream(), key, initiator: false);
        var viewer = new StreamViewerWindow("Me", (Initials("Me"), RedBrush));
        viewer.Show();
        var watching = Task.Run(() => StreamProtocol.WatchAsync(viewerChannel, viewer.ShowFrame, viewer.Stopped));
        var (watch, paced) = await StreamProtocol.AnswerAsync(hostChannel, CancellationToken.None);
        _streamInvites.Add("self"); // as while a friend's invitation is out: going live waits for them
        bool live = watch && GoLive();
        _streamInvites.Remove("self");
        if (!live) { Diag.Log("Stream to self: couldn't go live"); return; }
        _stream!.Add("self", "Me", hostChannel, paced);
        try { viewer.Ended(await watching ? "Me stopped streaming" : "The stream stopped"); }
        catch (Exception ex) { Diag.Log("Stream to self: " + ex.Message); }
    }

    /// <summary>Who's watching us, for our activity; null when we're not live.</summary>
    string? StreamingTo() => _stream?.Names is { Count: > 0 } names ? "to " + string.Join(", ", names) : null;

    bool Streaming => _stream != null;

    // ───────────── going live ─────────────

    /// <summary>Invites a friend to watch our screen; we go live once they click Watch.</summary>
    async void StreamTo(Friend friend)
    {
        if (!_settings.Beam) { ShowPeek("", "Turn on Beam to stream", "", null); return; }
        if (_stream?.Has(friend.Key) == true) { ShowPeek("", $"{friend.Name} is watching already", "", null); return; }
        if (!_streamInvites.Add(friend.Key)) return; // asking already
        ShowPeek("", $"Asking {friend.Name} to watch…", "", null, seconds: 4);
        try
        {
            byte[] secret = ShareCode.NewSecret();
            using var wait = new CancellationTokenSource(FriendsService.StreamPatience + TimeSpan.FromSeconds(10));
            await _friends.InviteToStreamAsync(friend, secret, wait.Token);
            var (host, port) = RelayEndpoint();
            var link = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), FriendsService.StreamPatience, wait.Token);
            var channel = new SecureChannel(link, ShareCode.Key(secret), initiator: true);
            bool watching, paced;
            try { (watching, paced) = await StreamProtocol.AnswerAsync(channel, wait.Token); }
            catch { channel.Dispose(); throw; }
            if (!watching)
            {
                channel.Dispose();
                ShowPeek("", $"{friend.Name} can't watch now", "", null);
                return;
            }
            if (!GoLive())
            {
                channel.Dispose();
                ShowPeek("", "Couldn't read the screen", "", RedBrush);
                return;
            }
            _stream!.Add(friend.Key, friend.Name, channel, paced);
            ShowPeek("", $"{friend.Name} is watching", "LIVE", RedBrush);
        }
        catch (OperationCanceledException)
        {
            ShowPeek("", $"{friend.Name} didn't answer", "", null);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            Diag.Log("Stream invitation: " + ex.Message);
            ShowPeek("", $"Couldn't reach {friend.Name}", "", RedBrush);
        }
        finally
        {
            _streamInvites.Remove(friend.Key);
            if (_stream != null && _stream.Names.Count == 0 && _streamInvites.Count == 0) StopStreaming();
        }
    }

    /// <summary>Starts taking pictures of the notch's screen, with the badge, unless we're live already. False when the screen can't be read.</summary>
    bool GoLive()
    {
        if (_stream != null) return true;
        if (ChosenScreen() is not { } screen) return false;
        var area = screen.Bounds;
        var (width, height) = StreamProtocol.FitWithin(Math.Max(1, area.Width), Math.Max(1, area.Height), StreamMaxSide);
        if (_screen.OpenGrabber(area.X, area.Y, area.Width, area.Height, width, height) is not { } grabber) return false;

        var host = _stream = new StreamHost();
        host.ViewersChanged += () => Dispatcher.UIThread.Post(OnViewersChanged);
        _streamStarted = DateTime.UtcNow;
        var stop = _captureStop = new CancellationTokenSource();
        // Capturing never waits for the network.
        var thread = new Thread(() => CaptureLoop(host, grabber, area, width / (double)Math.Max(1, area.Width), stop.Token)) { IsBackground = true, Name = "Holm stream capture" };
        thread.Start();

        _badge = new StreamBadge(StopStreaming);
        _badge.Opened += (_, _) => { PlaceBadge(); KeepOutOfStream(); };
        _badge.Show();
        KeepOutOfStream();
        OnViewersChanged();
        Achieve(AchievementBook.Streams);
        Diag.Log("Streaming started");
        return true;
    }

    /// <summary>
    /// Your messages and chats stay out of the picture: the notch, the badge and the Holm window are left out where the
    /// system can (Windows); elsewhere the capture thread blacks out where they are.
    /// </summary>
    void KeepOutOfStream()
    {
        if (_stream == null) return;
        bool hidden = true;
        foreach (var window in HolmWindows())
            if (window.TryGetPlatformHandle()?.Handle is { } handle) hidden &= _screen.HideFromCapture(handle, true);
        _hiddenFromCapture = hidden;
        UpdateStreamMask();
        if (!hidden && _maskTick == null)
        {
            _maskTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _maskTick.Tick += (_, _) => UpdateStreamMask();
            _maskTick.Start();
        }
    }

    IEnumerable<Window> HolmWindows()
    {
        yield return this;
        if (_badge != null) yield return _badge;
        if (_settingsWindow != null) yield return _settingsWindow;
    }

    /// <summary>Where Holm's windows are now, for the capture thread (nothing where the system hides them itself).</summary>
    void UpdateStreamMask()
    {
        if (_stream == null || _hiddenFromCapture) { _streamMask = Array.Empty<PixelRect>(); return; }
        var rects = new List<PixelRect>();
        foreach (var window in HolmWindows().Where(w => w.IsVisible))
        {
            // The notch's window is bigger than the notch (room for it to open): only the notch itself, a little wider.
            var rect = window == this && _island.TranslatePoint(new Point(0, 0), this) is { } at
                ? new Rect(at, _island.Bounds.Size).Inflate(8)
                : new Rect(window.Bounds.Size);
            rects.Add(new PixelRect(window.Position + PixelPoint.FromPoint(rect.Position, window.RenderScaling), PixelSize.FromSize(rect.Size, window.RenderScaling)));
        }
        _streamMask = rects.ToArray();
    }

    void CaptureLoop(StreamHost host, IScreenGrabber grabber, PixelRect area, double scale, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        int quality = StreamProtocol.StartQuality;
        long receipts = 0;
        using (grabber)
        {
            while (!stop.IsCancellationRequested)
            {
                long start = clock.ElapsedMilliseconds;
                try
                {
                    host.DropSilent();
                    if (host.Receipts != receipts)
                    {
                        receipts = host.Receipts;
                        quality = StreamProtocol.NextQuality(quality, host.SlowestRoundTripMs);
                    }
                    // No viewer can take a frame yet: a picture now would only be thrown away.
                    if (host.AnyReady && grabber.Grab() is { } picture)
                    {
                        BlackOut(picture, _streamMask, area, scale);
                        byte[] jpeg = Jpeg(picture, quality);
                        if (jpeg.Length < SecureChannel.MaxPlaintext) host.Publish(jpeg);
                        else quality = StreamProtocol.MinQuality; // a very busy screen: smaller pictures from now on
                    }
                }
                catch (Exception ex) { Diag.Log("Stream capture: " + ex.Message); }
                int wait = FrameMs - (int)(clock.ElapsedMilliseconds - start);
                if (wait > 0) stop.WaitHandle.WaitOne(wait);
            }
        }
    }

    /// <summary>Paints Holm's own windows black in a picture of <paramref name="area"/> taken at <paramref name="scale"/>.</summary>
    static void BlackOut(ScreenImage picture, PixelRect[] windows, PixelRect area, double scale)
    {
        foreach (var window in windows)
        {
            var over = window.Intersect(area);
            if (over.Width <= 0 || over.Height <= 0) continue;
            int left = (int)Math.Floor((over.X - area.X) * scale), top = (int)Math.Floor((over.Y - area.Y) * scale);
            int right = Math.Min(picture.Width, (int)Math.Ceiling((over.Right - area.X) * scale));
            int bottom = Math.Min(picture.Height, (int)Math.Ceiling((over.Bottom - area.Y) * scale));
            for (int y = Math.Max(0, top); y < bottom; y++)
            {
                var row = picture.Bgra.AsSpan((y * picture.Width + Math.Max(0, left)) * 4, (right - Math.Max(0, left)) * 4);
                for (int i = 0; i < row.Length; i += 4) { row[i] = 0; row[i + 1] = 0; row[i + 2] = 0; }
            }
        }
    }

    static byte[] Jpeg(ScreenImage picture, int quality)
    {
        var info = new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var image = SKImage.FromPixelCopy(info, picture.Bgra);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100));
        return data.ToArray();
    }

    void OnViewersChanged()
    {
        if (_stream == null) return;
        var names = _stream.Names;
        if (names.Count == 0 && _streamInvites.Count == 0)
        {
            StopStreaming();
            return;
        }
        _badge?.ShowViewers(names);
        PlaceBadge();
    }

    /// <summary>Under the notch, in the middle of its screen.</summary>
    void PlaceBadge()
    {
        if (_badge == null) return;
        double scaling = RenderScaling;
        double badgeWidth = _badge.Bounds.Width * _badge.RenderScaling;
        _badge.Position = new PixelPoint((int)(Position.X + (Bounds.Width * scaling - badgeWidth) / 2), (int)(Position.Y + 40 * scaling));
        UpdateStreamMask();
    }

    void StopStreaming()
    {
        if (_stream is not { } host) return;
        _stream = null;
        _captureStop?.Cancel();
        _captureStop = null;
        _streamMask = Array.Empty<PixelRect>();
        _maskTick?.Stop();
        _maskTick = null;
        _ = host.DisposeAsync().AsTask();
        long minutes = (long)(DateTime.UtcNow - _streamStarted).TotalMinutes;
        if (minutes > 0) Achieve(AchievementBook.StreamMinutes, minutes);
        foreach (var window in HolmWindows())
            if (window.TryGetPlatformHandle()?.Handle is { } handle) _screen.HideFromCapture(handle, false);
        _badge?.Close();
        _badge = null;
        ShowPeek("", "Stream ended", "", null);
        Diag.Log("Streaming stopped");
    }

    // ───────────── watching ─────────────

    void OnStreamOffered(Friend friend, byte[] secret)
    {
        if (_busy || _installingUpdate || _hiddenForFullscreen || !_settings.Beam)
        {
            Diag.Log("Declining a stream: busy");
            _ = DeclineStreamAsync(secret);
            return;
        }
        if (_streamOffer is { } earlier) _ = DeclineStreamAsync(earlier.Secret); // one at a time
        _streamOffer = (friend, secret);
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"{friend.Name} is streaming", "Watch their screen?", null,
                 ("Watch", () => AnswerStream(watch: true)), ("Not now", () => AnswerStream(watch: false)), textIcon: true);
        _taskHide.Stop(); // it waits for an answer
        if (_offerTimeout == null)
        {
            _offerTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            _offerTimeout.Tick += (_, _) => { _offerTimeout.Stop(); if (_streamOffer != null) AnswerStream(watch: false); };
        }
        _offerTimeout.Stop();
        _offerTimeout.Start();
    }

    void AnswerStream(bool watch)
    {
        _offerTimeout?.Stop();
        if (_streamOffer is not { } offer) return;
        _streamOffer = null;
        HideTask();
        if (watch) _ = WatchStreamAsync(offer.Friend, offer.Secret);
        else _ = DeclineStreamAsync(offer.Secret);
    }

    async Task WatchStreamAsync(Friend friend, byte[] secret)
    {
        var viewer = new StreamViewerWindow(friend.Name, (FaceText(friend.Name), FaceBrush(friend.Name)));
        viewer.Show();
        viewer.Activate();
        bool counted = false;
        try
        {
            var (host, port) = RelayEndpoint();
            using var link = CancellationTokenSource.CreateLinkedTokenSource(viewer.Stopped);
            link.CancelAfter(TimeSpan.FromSeconds(30));
            var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), TimeSpan.FromSeconds(30), link.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            bool ended = await StreamProtocol.WatchAsync(channel, frame =>
            {
                viewer.ShowFrame(frame);
                if (counted) return;
                counted = true;
                Dispatcher.UIThread.Post(() => Achieve(AchievementBook.Watched));
            }, viewer.Stopped);
            viewer.Ended(ended ? $"{friend.Name} stopped streaming" : "The stream stopped");
        }
        catch (OperationCanceledException) when (viewer.Stopped.IsCancellationRequested) { } // closed the window
        catch (OperationCanceledException) { viewer.Ended($"Couldn't reach {friend.Name}'s stream"); }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            Diag.Log("Watching a stream: " + ex.Message);
            if (!viewer.Stopped.IsCancellationRequested) viewer.Ended("The stream stopped");
        }
    }

    async Task DeclineStreamAsync(byte[] secret)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var (host, port) = RelayEndpoint();
            var stream = await RelayLink.ConnectAsync(host, port, ShareCode.RelayToken(secret), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            await StreamProtocol.DeclineAsync(channel, cts.Token);
        }
        catch (Exception ex) { Diag.Log("Couldn't decline a stream: " + ex.Message); }
    }

    /// <summary>Friends › Stream my screen to › (each friend), and Stop streaming while live.</summary>
    void FillStreamMenu(MenuItem menu)
    {
        if (Streaming)
        {
            var stop = new MenuItem { Header = "Stop streaming" };
            stop.Click += (_, _) => StopStreaming();
            menu.Items.Add(stop);
        }
        var to = new MenuItem { Header = Streaming ? "Add a viewer" : "Stream my screen to" };
        foreach (var friend in _friends.Friends.ToList())
        {
            var item = new MenuItem { Header = friend.Name.Replace("_", "__"), IsEnabled = _stream?.Has(friend.Key) != true };
            item.Click += (_, _) => StreamTo(friend);
            to.Items.Add(item);
        }
        menu.Items.Add(to);
    }
}
