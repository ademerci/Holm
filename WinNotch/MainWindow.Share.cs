using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// AirDrop for WinNotch: drop files on the notch and pick a person on the same network, or get a code
/// that works anywhere (through the relay). The receiver's notch always asks first, and files land in Downloads.
/// </summary>
public partial class MainWindow
{
    /// <summary>What a chip in the share view stands for: a person nearby, or "send with a code" (no peer).</summary>
    sealed record ShareTarget(NearbyPeer? Peer);

    static readonly Brush ShareBlue = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    static readonly Brush ChipBrush = Frozen(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    static readonly Color[] AvatarColors =
    {
        Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x5E, 0x5C, 0xE6),
        Color.FromRgb(0xFF, 0x2D, 0x55), Color.FromRgb(0x32, 0xAD, 0xE6), Color.FromRgb(0xFF, 0x9F, 0x0A),
    };

    NearbyService? _nearby;
    DispatcherTimer _shareWatch = null!, _taskHide = null!;
    bool _sharing, _dragInside, _taskVisible, _busy;
    DateTime _lastDragEvent, _lastProgressUi;
    string[] _dragFiles = Array.Empty<string>();
    ShareTarget? _hover;
    string? _shownPeers;
    CancellationTokenSource? _taskCts;
    Action? _primaryAction, _secondaryAction;
    readonly HashSet<string> _ownCodes = new();
    string? _lastOfferedCode;
    IReadOnlyList<NearbyPeer>? _demoPeers = null; // README screenshots only (set in Debug builds)

    bool TaskShowing => _taskVisible;

    void InitSharing()
    {
        // Windows calls DragOver only while the mouse moves, so a still mouse over a chip is not a finished drag.
        // The share view closes once the drag has left the notch (DragLeave without a new DragEnter), or, should
        // that never arrive, once no mouse button is held any more.
        _shareWatch = MakeTimer(200, () =>
        {
            var quiet = DateTime.UtcNow - _lastDragEvent;
            bool over = _dragInside
                ? quiet > TimeSpan.FromMilliseconds(500) && !Native.MouseButtonDown()
                : quiet > TimeSpan.FromMilliseconds(250);
            if (over) EndShare();
        });
        _taskHide = MakeTimer(5000, HideTask, oneShot: true);
    }

    void StartSharing()
    {
        if (!_settings.AirDrop || _nearby != null) return;
        var nearby = new NearbyService(Native.DisplayName()) { Incoming = OnNearbyIncoming };
        nearby.PeersChanged += () => Dispatcher.InvokeAsync(() => { if (_sharing) { RefreshShareTargets(); ApplyState(); } });
        try
        {
            nearby.Start();
            _nearby = nearby;
        }
        catch (Exception ex)
        {
            Diag.Log("Nearby sharing unavailable: " + ex.Message);
            nearby.Dispose();
        }
    }

    void StopSharing()
    {
        _nearby?.Dispose();
        _nearby = null;
        _taskCts?.Cancel();
    }

    (string Host, int Port) RelayEndpoint()
    {
        var relay = _settings.Relay;
        if (!string.IsNullOrWhiteSpace(relay))
        {
            int colon = relay.LastIndexOf(':');
            if (colon > 0 && int.TryParse(relay[(colon + 1)..], out int port) && port is > 0 and < 65536)
                return (relay[..colon], port);
        }
        return (RelayLink.DefaultHost, RelayLink.DefaultPort);
    }

    // ───────────── dragging files onto the notch ─────────────

    void Island_DragEnter(object sender, DragEventArgs e) => OnDrag(e);
    void Island_DragOver(object sender, DragEventArgs e) => OnDrag(e);

    void Island_DragLeave(object sender, DragEventArgs e)
    {
        // Also raised when moving between elements inside the notch, right before their DragEnter.
        _dragInside = false;
        _lastDragEvent = DateTime.UtcNow;
    }

    void OnDrag(DragEventArgs e)
    {
        e.Handled = true;
        if (!_settings.AirDrop || _busy || _hiddenForFullscreen || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = DragDropEffects.Copy;
        _lastDragEvent = DateTime.UtcNow;
        _dragInside = true;
        HighlightChip(e.OriginalSource as DependencyObject);
        if (_sharing) return;

        _dragFiles = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        Diag.Log($"Drag entered with {_dragFiles.Length} file(s)");
        _sharing = true;
        if (_taskVisible) HideTask(); // e.g. an old "Sent" message
        SetExpanded(false);
        RefreshShareTargets();
        ApplyState();
        _shareWatch.Start();
        _ = RefreshPeersAsync();
    }

    async Task RefreshPeersAsync()
    {
        if (_nearby == null) return;
        try { await _nearby.RefreshAsync(); } catch { return; }
        if (_sharing) { RefreshShareTargets(); ApplyState(); }
    }

    void EndShare()
    {
        _shareWatch.Stop();
        if (!_sharing) return;
        Diag.Log("Share view closed");
        _sharing = false;
        ApplyState();
    }

    void Island_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        var dropped = e.Data.GetData(DataFormats.FileDrop) as string[] ?? _dragFiles;
        var target = TargetOf(e.OriginalSource as DependencyObject);
        Diag.Log($"Dropped at {e.GetPosition(Island)} on {e.OriginalSource?.GetType().Name} -> {(target == null ? "nothing" : target.Peer?.Name ?? "Code")}");
        EndShare();
        if (!_settings.AirDrop || _busy) return;

        var paths = dropped.Where(File.Exists).ToList();
        if (paths.Count == 0)
        {
            ShowPeek("", dropped.Length > 0 ? "Folders can't be sent – zip them first" : "Nothing to send", "", null, false, null);
            return;
        }
        if (target == null)
        {
            ShowPeek("", "Drop it on a person or on “Code”", "", null, false, null);
            return;
        }
        if (target.Peer != null) _ = SendNearbyAsync(target.Peer, paths);
        else _ = SendWithCodeAsync(paths);
    }

    static ShareTarget? TargetOf(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is FrameworkElement { Tag: ShareTarget target }) return target;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    void HighlightChip(DependencyObject? over)
    {
        var target = TargetOf(over);
        if (target != _hover) Diag.Log($"Over {(target == null ? "nothing" : target.Peer?.Name ?? "Code")} ({over?.GetType().Name})");
        _hover = target;
        foreach (var chip in ShareTargets.Children.OfType<Button>())
            chip.Background = target != null && ReferenceEquals(chip.Tag, target) ? ShareBlue : ChipBrush;
    }

    void RefreshShareTargets()
    {
        int files = _dragFiles.Length;
        ShareTitle.Text = files == 1 ? $"Send “{Path.GetFileName(_dragFiles[0])}”" : $"Send {files} files";
        var peers = _demoPeers ?? _nearby?.Peers ?? Array.Empty<NearbyPeer>();
        ShareHint.Text = peers.Count == 0
            ? "No one with WinNotch on this network – drop it on Code to send it anywhere."
            : "Drop it on a person, or on Code to send it anywhere.";

        // Same people as shown: keep the chips, so the one under a still mouse stays highlighted.
        string shown = string.Join("|", peers.Take(6).Select(p => $"{p.Id} {p.Name} {p.Endpoint}"));
        if (shown == _shownPeers && ShareTargets.Children.Count > 0) return;
        _shownPeers = shown;
        Diag.Log($"Share targets: {peers.Count} nearby");

        ShareTargets.Children.Clear();
        foreach (var peer in peers.Take(6))
            ShareTargets.Children.Add(Chip(Avatar(Initials(peer.Name), AvatarColor(peer.Name)), peer.Name, new ShareTarget(peer)));
        ShareTargets.Children.Add(Chip(Avatar("", ShareBlue, icon: true), "Code", new ShareTarget(null)));
    }

    Button Chip(UIElement avatar, string name, ShareTarget target)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(avatar);
        content.Children.Add(new TextBlock
        {
            Text = name, FontSize = 12, FontWeight = FontWeights.SemiBold, MaxWidth = 110, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
        });
        return new Button { Style = (Style)FindResource("ChipButton"), Content = content, Tag = target, Background = ChipBrush };
    }

    static Border Avatar(string text, Brush background, bool icon = false) => new()
    {
        Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = background,
        Child = new TextBlock
        {
            Text = text, FontSize = icon ? 14 : 12, FontWeight = FontWeights.SemiBold,
            FontFamily = icon ? IconFont : new FontFamily("Segoe UI Variable Text, Segoe UI"),
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string initials = parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1],
            _ => string.Concat(parts[0][..1], parts[^1][..1]),
        };
        return initials.ToUpperInvariant();
    }

    static Brush AvatarColor(string name)
    {
        int hash = 0;
        foreach (char c in name) hash = hash * 31 + c;
        return Frozen(AvatarColors[(hash & int.MaxValue) % AvatarColors.Length]);
    }

    // ───────────── sending ─────────────

    async Task SendNearbyAsync(NearbyPeer peer, List<string> paths)
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, $"Waiting for {peer.Name}…", Summary(paths), null, null, ("Cancel", CancelTask));
        try
        {
            await using var channel = await NearbyService.ConnectAsync(peer, cts.Token);
            bool accepted = await FileTransfer.SendAsync(channel, Native.DisplayName(), paths,
                new Progress<double>(p => ShowProgress($"Sending to {peer.Name}", Summary(paths), p, sending: true)), cts.Token);
            if (accepted) EndTask("", GreenBrush, $"Sent to {peer.Name}", Summary(paths));
            else EndTask("", RedBrush, $"{peer.Name} declined", Summary(paths));
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't send"); }
    }

    async Task SendWithCodeAsync(List<string> paths)
    {
        var cts = BeginTask();
        byte[] secret = ShareCode.NewSecret();
        string code = ShareCode.Format(secret);
        _ownCodes.Add(code);
        bool copied = TrySetClipboard($"WinNotch AirDrop code: {code}");
        ShowTask("", ShareBlue, copied ? "Code copied – paste it to your friend" : "Send this code to your friend", code, null, null, ("Cancel", CancelTask));
        try
        {
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), TimeSpan.FromMinutes(15), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
            ShowTask("", ShareBlue, "Your friend is connected", "Waiting for them to accept…", null, null, ("Cancel", CancelTask));
            bool accepted = await FileTransfer.SendAsync(channel, Native.DisplayName(), paths,
                new Progress<double>(p => ShowProgress("Sending", Summary(paths), p, sending: true)), cts.Token);
            if (accepted) EndTask("", GreenBrush, "Sent", Summary(paths));
            else EndTask("", RedBrush, "Your friend declined", Summary(paths));
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, "The code expired", "No one used it within 15 minutes");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't send"); }
    }

    // ───────────── receiving ─────────────

    /// <summary>Someone on the network connected (worker thread). We answer on the UI thread; busy means no.</summary>
    Task OnNearbyIncoming(SecureChannel channel, string from)
        => Dispatcher.InvokeAsync(async () =>
        {
            if (_busy || _hiddenForFullscreen || !_settings.AirDrop)
            {
                await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(false), Native.DownloadsFolder(), null, CancellationToken.None);
                return;
            }
            var cts = BeginTask();
            try { await ReceiveIntoDownloadsAsync(channel, cts); }
            catch (Exception ex) { Fail(ex, cts, "Couldn't receive"); }
        }).Task.Unwrap();

    /// <summary>A copied code: offer to fetch the file. Our own codes and ones we already asked about are skipped.</summary>
    void OnClipboardChanged()
    {
        // Let the app that copied finish with the clipboard first.
        Dispatcher.InvokeAsync(() =>
        {
            if (!_settings.AirDrop || _busy || _taskVisible || _hiddenForFullscreen) return;
            if (!ShareCode.TryFind(ReadClipboardText(), out var secret, out var code)) return;
            if (_ownCodes.Contains(code) || code == _lastOfferedCode) return;
            _lastOfferedCode = code;
            ShowTask("", ShareBlue, "WinNotch code copied", "Get the file your friend is sending?", null,
                     ("Receive", () => _ = ReceiveWithCodeAsync(secret)), ("Not now", HideTask));
            _taskHide.Interval = TimeSpan.FromSeconds(20);
            _taskHide.Start();
        }, DispatcherPriority.Background);
    }

    async Task ReceiveWithCodeAsync(byte[] secret)
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, "Connecting…", "Waiting for the sender", null, null, ("Cancel", CancelTask));
        try
        {
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), TimeSpan.FromMinutes(3), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            await ReceiveIntoDownloadsAsync(channel, cts);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, "No sender found", "The code expired or was already used");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't receive"); }
    }

    async Task ReceiveIntoDownloadsAsync(SecureChannel channel, CancellationTokenSource cts)
    {
        string sender = "";
        // The transfer code continues on worker threads, so the question has to be asked on the UI thread.
        var saved = await FileTransfer.ReceiveAsync(channel,
            offer => Dispatcher.InvokeAsync(() => { sender = offer.Sender; return AskAsync(offer, cts.Token); }).Task.Unwrap(),
            Native.DownloadsFolder(),
            new Progress<double>(p => ShowProgress($"Receiving from {sender}", "Saving to Downloads", p, sending: false)), cts.Token);

        if (saved == null) { EndTask("", RedBrush, "Declined", $"Nothing was saved from {sender}"); return; }
        string first = saved[0];
        EndTask("", GreenBrush, saved.Count == 1 ? $"Saved {Path.GetFileName(first)}" : $"Saved {saved.Count} files",
                $"From {sender} · in Downloads", ("Show", () => { HideTask(); try { Shell.Reveal(first); } catch (Exception ex) { Diag.Error(ex); } }));
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    /// <summary>The question in the notch. No answer within a minute counts as no.</summary>
    async Task<bool> AskAsync(ShareOffer offer, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ShowTask(Initials(offer.Sender), AvatarColor(offer.Sender), $"{offer.Sender} wants to send you", Summary(offer), null,
                 ("Accept", () => answer.TrySetResult(true)), ("Decline", () => answer.TrySetResult(false)), textIcon: true);
        using (ct.Register(() => answer.TrySetResult(false)))
        {
            var finished = await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None));
            bool accepted = finished == answer.Task && answer.Task.Result;
            if (accepted) ShowTask("", ShareBlue, $"Receiving from {offer.Sender}", "Saving to Downloads", 0, null, ("Cancel", CancelTask));
            return accepted;
        }
    }

    // ───────────── the task view ─────────────

    CancellationTokenSource BeginTask()
    {
        _taskHide.Stop();
        _taskHide.Interval = TimeSpan.FromSeconds(5);
        _taskCts?.Dispose();
        _taskCts = new CancellationTokenSource();
        _busy = true;
        return _taskCts;
    }

    void CancelTask() => _taskCts?.Cancel();

    void ShowTask(string icon, Brush circle, string title, string subtitle, double? progress,
                  (string Label, Action Act)? primary, (string Label, Action Act)? secondary, bool textIcon = false)
    {
        TaskIcon.Text = icon;
        TaskIcon.FontFamily = textIcon ? new FontFamily("Segoe UI Variable Text, Segoe UI") : IconFont;
        TaskIcon.FontWeight = textIcon ? FontWeights.SemiBold : FontWeights.Normal;
        TaskIconCircle.Background = circle;
        TaskTitle.Text = title;
        TaskSubtitle.Text = subtitle;
        TaskSubtitle.Visibility = subtitle.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TaskProgressHost.Visibility = progress == null ? Visibility.Collapsed : Visibility.Visible;
        TaskProgress.Value = progress ?? 0;
        TaskProgress.Fill = ShareBlue;

        TaskPrimary.Content = primary?.Label;
        TaskPrimary.Visibility = primary == null ? Visibility.Collapsed : Visibility.Visible;
        _primaryAction = primary?.Act;
        TaskSecondary.Content = secondary?.Label;
        TaskSecondary.Visibility = secondary == null ? Visibility.Collapsed : Visibility.Visible;
        _secondaryAction = secondary?.Act;

        _taskHide.Stop();
        if (!_taskVisible)
        {
            _taskVisible = true;
            _peekActive = false;
            _volumePeek = false;
            SetExpanded(false);
        }
        ApplyState();
    }

    /// <summary>Progress, at most a few times a second (every 64 KB would be far too many redraws).</summary>
    void ShowProgress(string title, string subtitle, double fraction, bool sending)
    {
        if (!_busy) return;
        var now = DateTime.UtcNow;
        if (fraction > 0 && fraction < 1 && now - _lastProgressUi < TimeSpan.FromMilliseconds(150)) return;
        _lastProgressUi = now;
        ShowTask(sending ? "" : "", ShareBlue, title, $"{subtitle} · {Math.Round(fraction * 100)}%", fraction, null, ("Cancel", CancelTask));
    }

    void EndTask(string icon, Brush circle, string title, string subtitle, (string Label, Action Act)? primary = null)
    {
        _busy = false;
        ShowTask(icon, circle, title, subtitle, null, primary, ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(5);
        _taskHide.Start();
    }

    void Fail(Exception ex, CancellationTokenSource cts, string title)
    {
        if (ex is OperationCanceledException && cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, "Cancelled", "");
            return;
        }
        Diag.Log($"{title}: {ex.GetType().Name}: {ex.Message}");
        string reason = ex switch
        {
            SocketException => "Couldn't reach them",
            AuthenticationTagMismatchException => "The data didn't check out – wrong code?",
            EndOfStreamException => "The connection was closed",
            IOException io => io.Message,
            _ => "Something went wrong",
        };
        EndTask("", RedBrush, title, reason);
    }

    void HideTask()
    {
        _taskHide.Stop();
        _primaryAction = _secondaryAction = null;
        if (!_taskVisible) return;
        _taskVisible = false;
        ApplyState();
    }

    void TaskPrimary_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _primaryAction?.Invoke();
    }

    void TaskSecondary_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _secondaryAction?.Invoke();
    }

    // ───────────── menu ─────────────

    void MenuAirDrop_Click(object sender, RoutedEventArgs e)
    {
        _settings.AirDrop = MenuAirDrop.IsChecked;
        _settings.Save();
        if (_settings.AirDrop) StartSharing();
        else StopSharing();
    }

    void MenuReceive_Click(object sender, RoutedEventArgs e)
    {
        if (!ShareCode.TryFind(ReadClipboardText(), out var secret, out var code))
        {
            ShowPeek("", "Copy your friend's WinNotch code first", "", null, false, null);
            return;
        }
        if (_ownCodes.Contains(code))
        {
            ShowPeek("", "That's your own code – send it to a friend", "", null, false, null);
            return;
        }
        _lastOfferedCode = code;
        _ = ReceiveWithCodeAsync(secret);
    }

    // ───────────── helpers ─────────────

    static string Summary(IReadOnlyList<string> paths)
    {
        long bytes = paths.Sum(p => { try { return new FileInfo(p).Length; } catch { return 0L; } });
        return paths.Count == 1 ? $"{Path.GetFileName(paths[0])} · {Size(bytes)}" : $"{paths.Count} files · {Size(bytes)}";
    }

    static string Summary(ShareOffer offer)
        => offer.Files.Count == 1 ? $"{offer.Files[0].Name} · {Size(offer.TotalBytes)}" : $"{offer.Files.Count} files · {Size(offer.TotalBytes)}";

    static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    static string? ReadClipboardText()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(30); } // another app has it open
        }
        return null;
    }

    static bool TrySetClipboard(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(30); }
        }
        return false;
    }
}
