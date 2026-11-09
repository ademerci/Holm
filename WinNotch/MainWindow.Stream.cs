using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Streaming your screen to friends, like going live in a chat app: they get an invitation in their notch with Watch,
/// and a window with your screen. While you're live a red badge sits under the notch with Stop. The notch, the badge
/// and the Holm window stay out of the picture, so your messages and chats don't go along.
/// </summary>
public partial class MainWindow
{
    const int FrameMs = 125; // up to 8 pictures a second; a slow connection gets fewer

    StreamHost? _stream;
    CancellationTokenSource? _captureStop;
    StreamBadge? _badge;
    DateTime _streamStarted;
    readonly System.Collections.Generic.HashSet<string> _streamInvites = new();
    (Friend Friend, byte[] Secret)? _streamOffer;
    DispatcherTimer? _offerTimeout;

    void InitStreaming() => _friends.StreamOffered += OnStreamOffered;

    /// <summary>Who's watching us, for our activity; null when we're not live.</summary>
    string? StreamingTo() => _stream?.Names is { Count: > 0 } names ? "to " + string.Join(", ", names) : null;

    bool Streaming => _stream != null;

    // ───────────── going live ─────────────

    /// <summary>Invites a friend to watch our screen; we go live once they click Watch.</summary>
    async void StreamTo(Friend friend)
    {
        if (!_settings.Beam) { ShowPeek("", "Turn on Beam to stream", "", null, false, null); return; }
        if (_stream?.Has(friend.Key) == true) { ShowPeek("", $"{friend.Name} is watching already", "", null, false, null); return; }
        if (!_streamInvites.Add(friend.Key)) return; // asking already
        ShowPeek("", $"Asking {friend.Name} to watch…", "", null, false, null, seconds: 4);
        try
        {
            byte[] secret = ShareCode.NewSecret();
            using var wait = new CancellationTokenSource(FriendsService.StreamPatience + TimeSpan.FromSeconds(10));
            await _friends.InviteToStreamAsync(friend, secret, wait.Token);
            var (host, port) = RelayEndpoint();
            var link = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), FriendsService.StreamPatience, wait.Token);
            var channel = new SecureChannel(link, ShareCode.Key(secret), initiator: true);
            bool watching;
            try { watching = await StreamProtocol.AnswerAsync(channel, wait.Token); }
            catch { channel.Dispose(); throw; }
            if (!watching)
            {
                channel.Dispose();
                ShowPeek("", $"{friend.Name} can't watch now", "", null, false, null);
                return;
            }
            GoLive();
            _stream!.Add(friend.Key, friend.Name, channel);
            ShowPeek("", $"{friend.Name} is watching", "LIVE", null, false, HubTheme.Red);
        }
        catch (OperationCanceledException)
        {
            ShowPeek("", $"{friend.Name} didn't answer", "", null, false, null);
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            Diag.Log("Stream invitation: " + ex.Message);
            ShowPeek("", $"Couldn't reach {friend.Name}", "", null, false, RedBrush);
        }
        finally
        {
            _streamInvites.Remove(friend.Key);
            if (_stream != null && _stream.Names.Count == 0 && _streamInvites.Count == 0) StopStreaming();
        }
    }

    /// <summary>Starts taking pictures of the notch's screen, with the badge, unless we're live already.</summary>
    void GoLive()
    {
        if (_stream != null) return;
        var screen = Native.Screens().FirstOrDefault(s => s.Handle == _monitor) ?? Native.Screens().First();
        var area = new Int32Rect(screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Right - screen.Bounds.Left, screen.Bounds.Bottom - screen.Bounds.Top);
        var host = _stream = new StreamHost();
        host.ViewersChanged += () => Dispatcher.InvokeAsync(OnViewersChanged);
        _streamStarted = DateTime.UtcNow;
        var stop = _captureStop = new CancellationTokenSource();
        // WPF's imaging wants a thread of its own (STA); capturing never waits for the network.
        var thread = new Thread(() => CaptureLoop(host, area, stop.Token)) { IsBackground = true, Name = "Holm stream capture" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // Your messages and chats stay out of the picture.
        ScreenCapture.HideFromCapture(_hwnd, true);
        if (_settingsWindow != null) ScreenCapture.HideFromCapture(new WindowInteropHelper(_settingsWindow).Handle, true);
        _badge = new StreamBadge(StopStreaming);
        _badge.Show();
        PlaceBadge();
        OnViewersChanged();
        Achieve(AchievementBook.Streams);
        Diag.Log("Streaming started");
    }

    static void CaptureLoop(StreamHost host, Int32Rect area, CancellationToken stop)
    {
        var clock = Stopwatch.StartNew();
        while (!stop.IsCancellationRequested)
        {
            long start = clock.ElapsedMilliseconds;
            try
            {
                if (ScreenCapture.Jpeg(area) is { } jpeg && jpeg.Length < SecureChannel.MaxPlaintext) host.Publish(jpeg);
            }
            catch (Exception ex) { Diag.Log("Stream capture: " + ex.Message); }
            int wait = FrameMs - (int)(clock.ElapsedMilliseconds - start);
            if (wait > 0) stop.WaitHandle.WaitOne(wait);
        }
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
    }

    /// <summary>Under the notch, in the middle of its screen.</summary>
    void PlaceBadge()
    {
        if (_badge == null) return;
        _badge.UpdateLayout();
        _badge.Left = Left + (ActualWidth - _badge.ActualWidth) / 2;
        _badge.Top = Top + 40;
    }

    void StopStreaming()
    {
        if (_stream is not { } host) return;
        _stream = null;
        _captureStop?.Cancel();
        _captureStop = null;
        _ = host.DisposeAsync().AsTask();
        long minutes = (long)(DateTime.UtcNow - _streamStarted).TotalMinutes;
        if (minutes > 0) Achieve(AchievementBook.StreamMinutes, minutes);
        ScreenCapture.HideFromCapture(_hwnd, false);
        if (_settingsWindow != null) ScreenCapture.HideFromCapture(new WindowInteropHelper(_settingsWindow).Handle, false);
        _badge?.Close();
        _badge = null;
        ShowPeek("", "Stream ended", "", null, false, null);
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
        _offerTimeout ??= MakeTimer(45_000, () => { if (_streamOffer != null) AnswerStream(watch: false); }, oneShot: true);
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
                viewer.Show(frame);
                if (counted) return;
                counted = true;
                Dispatcher.InvokeAsync(() => Achieve(AchievementBook.Watched));
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
    void FillStreamMenu()
    {
        if (Streaming)
        {
            var stop = new MenuItem { Header = "Stop streaming" };
            stop.Click += (_, _) => StopStreaming();
            MenuFriends.Items.Add(stop);
        }
        var to = new MenuItem { Header = Streaming ? "Add a viewer" : "Stream my screen to" };
        foreach (var friend in _friends.Friends.ToList())
        {
            var item = new MenuItem { Header = friend.Name.Replace("_", "__"), IsEnabled = _stream?.Has(friend.Key) != true };
            item.Click += (_, _) => StreamTo(friend);
            to.Items.Add(item);
        }
        MenuFriends.Items.Add(to);
    }
}

/// <summary>The red LIVE badge under the notch while streaming, with who's watching and Stop. Never in the stream itself.</summary>
internal sealed class StreamBadge : Window
{
    readonly TextBlock _who = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };

    public StreamBadge(Action stop)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = HubTheme.Text;
        Foreground = Brushes.White;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border
        {
            Background = HubTheme.Red, CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 2, 7, 3), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "● LIVE", FontSize = 11, FontWeight = FontWeights.Bold },
        });
        row.Children.Add(_who);
        var stopButton = new Border
        {
            Background = HubTheme.Selected, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), Cursor = Cursors.Hand,
            Child = new TextBlock { Text = "Stop", FontSize = 12, FontWeight = FontWeights.SemiBold },
        };
        stopButton.MouseLeftButtonUp += (_, _) => stop();
        row.Children.Add(stopButton);
        Content = new Border
        {
            Background = HubTheme.Frozen(Color.FromArgb(0xF2, 0x14, 0x14, 0x16)), CornerRadius = new CornerRadius(16),
            BorderBrush = HubTheme.Frozen(Color.FromArgb(0x66, 0xFF, 0x45, 0x3A)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 5, 6, 5), Child = row,
        };
        SourceInitialized += (_, _) => ScreenCapture.HideFromCapture(new WindowInteropHelper(this).Handle, true);
    }

    public void ShowViewers(System.Collections.Generic.IReadOnlyList<string> names)
    {
        _who.Text = names.Count switch
        {
            0 => "Waiting for viewers…",
            1 => $"{names[0]} is watching",
            _ => $"{names.Count} watching",
        };
        ToolTip = string.Join(", ", names);
    }
}
