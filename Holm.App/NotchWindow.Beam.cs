using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Holm.Platform;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Beam (the WPF app's MainWindow.Share.cs): files dragged onto the notch go to a friend (anywhere, through their
/// inbox and the relay), to someone with Holm on the same network, on the shelf, or with a code that is copied to
/// pass on. Coming the other way, the notch asks before anything is saved into Downloads. The transfer, the
/// encryption, the relay and the friends' inboxes are the WPF app's own code (WinNotch/Sharing, FriendsService).
/// </summary>
public sealed partial class NotchWindow
{
    /// <summary>What a chip in the share view stands for: a friend, a person nearby, or "send with a code" (neither).</summary>
    /// <param name="Place">For the chips that aren't people: <see cref="ShelfPlace"/> (or "send with a code" when null).</param>
    sealed record ShareTarget(NearbyPeer? Peer, Friend? Friend = null, string? Place = null)
    {
        public const string ShelfPlace = "shelf", PhonePlace = "phone";
        public string Name => Friend?.Name ?? Peer?.Name ?? Place switch { ShelfPlace => "Shelf", PhonePlace => "Phone", _ => "Code" };
    }

    static readonly Color[] AvatarColors =
    {
        Color.FromRgb(0xFF, 0x7A, 0x59), Color.FromRgb(0x34, 0xC7, 0x59), Color.FromRgb(0x5E, 0x5C, 0xE6),
        Color.FromRgb(0xFF, 0x2D, 0x55), Color.FromRgb(0x32, 0xAD, 0xE6), Color.FromRgb(0xFF, 0x9F, 0x0A),
    };

    NearbyService? _nearby;
    FriendsService _friends = null!;
    readonly List<Friend> _addBack = new(); // people who added us while the notch was busy
    readonly List<(Friend Card, string RequestId)> _friendRequests = new();
    string? _shownFriendRequestId;
    readonly DispatcherTimer _shareWatch = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly DispatcherTimer _pickClose = new() { Interval = TimeSpan.FromMilliseconds(900) };
    bool _busy, _dragInside;
    DateTime _lastDragEvent, _lastProgressUi;
    string[] _dragFiles = Array.Empty<string>();
    string[]? _pickFiles; // the share view was opened to pick where these go, by clicking
    ShareTarget? _hover;
    string? _shownPeers;
    readonly Dictionary<string, Control> _onlineDots = new(); // by friend key, on the chips shown now
    CancellationTokenSource? _taskCts;
    readonly HashSet<string> _ownCodes = new();
    string? _lastOfferedCode, _lastOfferedFriend;
    long _acceptTick;
    readonly Dictionary<string, IBrush?> _pictures = new();

    /// <summary>ZIPs left over from a Holm that was closed mid-send (the WPF app's ForgetLeftoverZips).</summary>
    static void ForgetLeftoverZips()
    {
        try
        {
            if (Directory.Exists(FolderPack.TempFolder))
                FolderPack.Forget(Directory.GetFiles(FolderPack.TempFolder, "*.zip"), FolderPack.TempFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void InitBeam(IPlatformServices platform)
    {
        ForgetLeftoverZips();
        FriendsService.ProtectKey = platform.Identity.Protect;
        FriendsService.UnprotectKey = platform.Identity.Unprotect;
        FriendsService.AccountName = platform.Identity.AccountName;
        _friends = new FriendsService(_settings, action => Dispatcher.UIThread.InvokeAsync(action).GetTask());
        _friends.FilesOffered += OnFriendFiles;
        _friends.AddedUs += OnFriendAddedUs;
        _friends.FriendRequested += OnFriendRequested;
        _friends.FriendRequestCancelled += OnFriendRequestCancelled;
        _friends.RemovedUs += OnFriendRemovedUs;
        _friends.ProfileReceived += _ => { _pictures.Clear(); if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); } };
        _friends.FriendOnline += _ => { if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); } };

        // A drag is over once it left the notch (DragLeave without a new DragEnter), or, should that never arrive,
        // once it has been still for a while.
        _shareWatch.Tick += (_, _) =>
        {
            var quiet = DateTime.UtcNow - _lastDragEvent;
            bool over = _dragInside ? quiet > TimeSpan.FromSeconds(3) : quiet > TimeSpan.FromMilliseconds(250);
            if (over) EndShare();
        };
        _pickClose.Tick += (_, _) => { _pickClose.Stop(); EndShare(); };
        // Picking with clicks: leaving means "never mind".
        _island.PointerExited += (_, _) => { if (_pickFiles != null) _pickClose.Start(); };
        _island.PointerEntered += (_, _) => _pickClose.Stop();

        DragDrop.SetAllowDrop(_island, true);
        _island.AddHandler(DragDrop.DragEnterEvent, OnDrag);
        _island.AddHandler(DragDrop.DragOverEvent, OnDrag);
        _island.AddHandler(DragDrop.DragLeaveEvent, (_, _) =>
        {
            // Also raised moving between elements inside the notch, right before their DragEnter.
            _dragInside = false;
            _lastDragEvent = DateTime.UtcNow;
        });
        _island.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            e.Handled = true;
            var dropped = DroppedPaths(e);
            var target = TargetOf(e.Source as Visual);
            Diag.Log($"Dropped on {target?.Name ?? "nothing"}");
            EndShare();
            SendTo(target, dropped.Count > 0 ? dropped.ToArray() : _dragFiles);
        });
    }

    void StartSharing()
    {
        if (!_settings.Beam) return;
        // A damaged friend card in the settings must not take the whole notch down.
        try { _friends.Restart(); }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException) { Diag.Log("Friends not started: " + ex.GetType().Name); }
        if (_nearby != null) return;
        var nearby = new NearbyService(_friends.MyName) { Incoming = OnNearbyIncoming };
        nearby.PeersChanged += () => Dispatcher.UIThread.Post(() => { if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); } });
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
        _friends.Stop();
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

    void SetBeam(bool on)
    {
        _settings.Beam = on;
        _settings.Save();
        if (on) StartSharing();
        else StopSharing();
    }

    // ───────────── dragging files onto the notch ─────────────

    void OnDrag(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        // Without Beam, the shelf is all there is to drop on. Not while dragging off the shelf.
        if (_draggingOut || _busy || _hiddenForFullscreen || PhoneShowing || !e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        _lastDragEvent = DateTime.UtcNow;
        _dragInside = true;
        HighlightChip(e.Source as Visual);
        if (_sharing) return;

        _dragFiles = DroppedPaths(e).ToArray();
        Diag.Log($"Drag entered with {_dragFiles.Length} file(s)");
        _sharing = true;
        if (_taskVisible) HideTask(); // e.g. an old "Sent" message
        EndTyping();
        SetExpanded(false);
        RefreshShareTargets();
        ApplyState(animate: true);
        _shareWatch.Start();
        _ = RefreshPeersAsync();
        if (_settings.Beam) _friends.PingAll(); // green dots for friends whose Holm is running, a second or two later
    }

    async Task RefreshPeersAsync()
    {
        if (_nearby == null) return;
        try { await _nearby.RefreshAsync(); } catch { return; }
        if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
    }

    void EndShare()
    {
        _shareWatch.Stop();
        _pickClose.Stop();
        _pickFiles = null;
        ForgetPickedText();
        if (!_sharing) return;
        Diag.Log("Share view closed");
        _sharing = false;
        HighlightChip(null);
        ApplyState(animate: true);
    }

    /// <summary>The share view, to click where the files go instead of dropping them; it closes when the mouse leaves.</summary>
    void PickTarget(string[] files)
    {
        HideTask();
        _pickFiles = files;
        _dragFiles = files;
        _sharing = true;
        SetExpanded(false);
        RefreshShareTargets();
        ApplyState(animate: true);
        if (!_island.IsPointerOver) _pickClose.Start();
        _ = RefreshPeersAsync();
        if (_settings.Beam) _friends.PingAll();
    }

    /// <summary>Files dropped (or, after a screenshot, clicked) onto a chip in the share view.</summary>
    void SendTo(ShareTarget? target, string[] dropped, bool text = false)
    {
        if (_draggingOut || _busy) return;
        if (text && target?.Place == ShareTarget.ShelfPlace)
        {
            ShowPeek(null, "A text can't go on the shelf", "", null);
            return;
        }
        if (target?.Place == ShareTarget.ShelfPlace)
        {
            AddToShelf(dropped); // folders too: only the paths are kept
            return;
        }
        if (!_settings.Beam)
        {
            ShowPeek("", "Drop it on “Shelf” to keep it here", "", null);
            return;
        }
        var paths = dropped.Where(File.Exists).ToList();
        var folders = _settings.SendFolders ? dropped.Where(Directory.Exists).ToList() : new List<string>();
        if (paths.Count == 0 && folders.Count == 0)
        {
            ShowPeek("", dropped.Length > 0 ? "Folders can't be sent – zip them first" : "Nothing to send", "", null);
            return;
        }
        if (target == null)
        {
            ShowPeek("", "Drop it on a person or on “Code”", "", null);
            return;
        }
        if (folders.Count > 0) _ = PackAndSendAsync(target, paths, folders);
        else if (text) _ = BeamTextFileAsync(target, paths[0]);
        else _ = SendFilesAsync(target, paths);
    }

    /// <summary>Sends to whoever the chip stands for; done when the transfer is over.</summary>
    Task SendFilesAsync(ShareTarget target, List<string> paths, bool text = false)
    {
        if (target.Place == ShareTarget.PhonePlace) { BeamToPhone(paths); return Task.CompletedTask; }
        if (target.Friend != null) return SendToFriendAsync(target.Friend, paths, text);
        if (target.Peer != null) return SendNearbyAsync(target.Peer, paths, text);
        return SendWithCodeAsync(paths, text);
    }

    /// <summary>A folder dropped on a person goes as a ZIP: packed into Holm's temp folder, sent like any file, deleted afterwards.</summary>
    async Task PackAndSendAsync(ShareTarget target, List<string> files, List<string> folders)
    {
        var cts = BeginTask();
        string name = folders.Count == 1 ? Path.GetFileName(folders[0].TrimEnd('\\', '/')) is { Length: > 0 } one ? one : folders[0] : $"{folders.Count} folders";
        ShowTask(FolderGlyph, ShareBlue, $"Packing {name}…", "As a ZIP, to send", 0, null, ("Cancel", CancelTask));
        var zips = new List<string>();
        int skipped = 0;
        try
        {
            for (int i = 0; i < folders.Count; i++)
            {
                int index = i;
                var progress = new Progress<double>(p => ShowProgress($"Packing {name}", "As a ZIP, to send", (index + p) / folders.Count, sending: true));
                var (zip, missed) = await Task.Run(() => FolderPack.Pack(folders[index], FolderPack.TempFolder, progress, cts.Token));
                zips.Add(zip);
                skipped += missed;
            }
        }
        catch (Exception ex)
        {
            FolderPack.Forget(zips, FolderPack.TempFolder);
            Fail(ex, cts, "Couldn't pack the folder");
            return;
        }
        if (skipped > 0) Diag.Log($"Packing: {skipped} file(s) couldn't be read and were left out");
        var all = files.Concat(zips).ToList();
        if (target.Place == ShareTarget.PhonePlace)
        {
            _busy = false;
            BeamToPhone(all);
            _phoneZips.AddRange(zips); // until the link closes
            return;
        }
        try { await SendFilesAsync(target, all); }
        finally { FolderPack.Forget(zips, FolderPack.TempFolder); }
    }

    static ShareTarget? TargetOf(Visual? element)
    {
        for (var at = element; at != null; at = at.GetVisualParent())
            if (at is Control { Tag: ShareTarget target }) return target;
        return null;
    }

    void HighlightChip(Visual? over)
    {
        var target = TargetOf(over);
        _hover = target;
        foreach (var chip in _shareTargets.Children.OfType<Border>())
            chip.Background = target != null && ReferenceEquals(chip.Tag, target) ? ShareBlue : ChipBrush;
    }

    void RefreshShareTargets()
    {
        int files = _dragFiles.Length;
        bool beam = _settings.Beam;
        string what = _pickText != null ? "the copied text" : files == 1 ? $"“{Path.GetFileName(_dragFiles[0].TrimEnd(Path.DirectorySeparatorChar))}”" : $"{files} files";
        _shareTitle.Text = beam ? "Send " + what : what;
        var peers = beam ? _nearby?.Peers ?? Array.Empty<NearbyPeer>() : Array.Empty<NearbyPeer>();
        var friends = beam ? _friends.Friends : Array.Empty<Friend>();
        int online = friends.Take(8).Count(_friends.IsOnline);
        _shareHint.Text = _pickFiles != null ? (beam ? "Click where it should go." : "Click Shelf to keep it here.")
            : !beam ? "Drop it on Shelf to keep it here and drag it out later."
            : friends.Count > 0
            ? "Drop it on a friend, on someone nearby, or on Code." + (online switch { 0 => "", 1 => " One friend is online.", _ => $" {online} friends are online." })
            : peers.Count == 0
                ? "No one with Holm on this network – drop it on Code to send it anywhere."
                : "Drop it on a person, or on Code to send it anywhere.";

        // Same people as shown: keep the chips, so the one under a still mouse stays highlighted.
        string shown = beam + "|" + string.Join("|", friends.Take(8).Select(f => $"{f.Key} {f.Name} {f.Color} {f.Picture?.GetHashCode()}")
                                                     .Concat(peers.Take(6).Select(p => $"{p.Id} {p.Name} {p.Endpoint}")));
        if (shown == _shownPeers && _shareTargets.Children.Count > 0)
        {
            ShowOnlineDots();
            return;
        }
        _shownPeers = shown;
        _shareTargets.Children.Clear();
        _onlineDots.Clear();
        foreach (var friend in friends.Take(8))
        {
            var chip = PersonChip(friend.Name, new ShareTarget(null, friend), isFriend: true);
            if (friend.Status != null) ToolTip.SetTip(chip, friend.Status);
            _shareTargets.Children.Add(chip);
        }
        foreach (var peer in peers.Take(6)) _shareTargets.Children.Add(PersonChip(peer.Name, new ShareTarget(peer)));
        if (beam) _shareTargets.Children.Add(PlaceChip("\uE8EA", PhoneIndigo, new ShareTarget(null, Place: ShareTarget.PhonePlace)));
        _shareTargets.Children.Add(PlaceChip(ShelfGlyph, ShelfGray, new ShareTarget(null, Place: ShareTarget.ShelfPlace)));
        if (beam) _shareTargets.Children.Add(PlaceChip("", ShareBlue, new ShareTarget(null)));
        ShowOnlineDots();
        if (_hover != null) HighlightChip(null);
    }

    void ShowOnlineDots()
    {
        foreach (var friend in _friends.Friends)
            if (_onlineDots.TryGetValue(friend.Key, out var dot)) dot.IsVisible = _friends.IsOnline(friend);
    }

    Border PersonChip(string name, ShareTarget target, bool isFriend = false)
    {
        Control avatar = Avatar(FaceText(name), FaceBrush(name));
        if (isFriend)
        {
            // A small star on the avatar tells friends (anywhere) from people who happen to be nearby.
            var badge = new Grid();
            badge.Children.Add(avatar);
            badge.Children.Add(new Border
            {
                Width = 13, Height = 13, CornerRadius = new CornerRadius(6.5), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -2),
                Child = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 8, Foreground = ShareBlue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            // A green dot: their Holm answered just now, so they'd see the files right away.
            var dot = new Border
            {
                Width = 11, Height = 11, CornerRadius = new CornerRadius(5.5), Background = GreenBrush,
                BorderBrush = Brushes.Black, BorderThickness = new Thickness(2), IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -2, 0),
            };
            badge.Children.Add(dot);
            _onlineDots[target.Friend!.Key] = dot;
            avatar = badge;
        }
        return ChipFor(avatar, name, target);
    }

    Border PlaceChip(string glyph, IBrush background, ShareTarget target) => ChipFor(Avatar(glyph, background, icon: true), target.Name, target);

    Border ChipFor(Control avatar, string name, ShareTarget target)
    {
        var chip = Chip(avatar, name);
        chip.Tag = target;
        // Picking where a screenshot goes: click instead of drop.
        chip.PointerPressed += (_, e) =>
        {
            if (_pickFiles is not { } files || !e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Diag.Log($"Picked {target.Name}");
            bool text = _pickText != null;
            _pickText = null; // it's the sending's now
            EndShare();
            SendTo(target, files, text);
        };
        chip.PointerEntered += (_, _) => { if (_pickFiles != null) HighlightChip(chip); };
        chip.PointerExited += (_, _) => { if (_pickFiles != null) HighlightChip(null); };
        return chip;
    }

    static Border Avatar(string text, IBrush background, bool icon = false) => new()
    {
        Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = background,
        Child = new TextBlock
        {
            Text = text, FontSize = icon ? 14 : 12, FontWeight = FontWeight.SemiBold,
            FontFamily = icon ? IconFont : FontFamily.Default, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    /// <summary>"LH" for Lena Hoffmann; whole letters, so an emoji or accented letter isn't cut in half.</summary>
    internal static string Initials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string First(string word) => new StringInfo(word).SubstringByTextElements(0, 1);
        string initials = parts.Length switch
        {
            0 => "?",
            1 => First(parts[0]),
            _ => string.Concat(First(parts[0]), First(parts[^1])),
        };
        return initials.ToUpperInvariant();
    }

    static IBrush AvatarColor(string name)
    {
        int hash = 0;
        foreach (char c in name) hash = hash * 31 + c;
        return new SolidColorBrush(AvatarColors[(hash & int.MaxValue) % AvatarColors.Length]).ToImmutable();
    }

    Friend? FriendNamed(string name) => _friends.Friends.FirstOrDefault(f => f.Name == name);

    /// <summary>What goes in someone's circle: nothing over their picture, else their initials.</summary>
    string FaceText(string name) => PictureOf(FriendNamed(name)?.Picture) != null ? "" : Initials(name);

    /// <summary>A friend's picture, else the color they chose, else the color their name gives.</summary>
    IBrush FaceBrush(string name)
    {
        var friend = FriendNamed(name);
        if (PictureOf(friend?.Picture) is { } picture) return picture;
        if (FriendProfile.CleanColor(friend?.Color) is { } color && Color.TryParse(color, out var parsed)) return new SolidColorBrush(parsed).ToImmutable();
        return AvatarColor(name);
    }

    IBrush? PictureOf(string? base64)
    {
        if (base64 == null) return null;
        if (_pictures.TryGetValue(base64, out var cached)) return cached;
        IBrush? brush = null;
        try { brush = new ImageBrush(new Bitmap(new MemoryStream(Convert.FromBase64String(base64)))) { Stretch = Stretch.UniformToFill }; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        return _pictures[base64] = brush;
    }

    // ───────────── sending ─────────────

    async Task SendNearbyAsync(NearbyPeer peer, List<string> paths, bool text = false)
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, $"Checking if {peer.Name} is available…", "Waiting for their notch to answer", null, null, ("Cancel", CancelTask));
        try
        {
            SecureChannel channel;
            try { channel = await NearbyService.ConnectAsync(peer, cts.Token); }
            catch (Exception ex) when (ex is IOException or SocketException || (ex is OperationCanceledException && !cts.IsCancellationRequested))
            {
                Diag.Log($"Nearby availability check for {peer.Name} failed: {ex.Message}");
                EndTask("", RedBrush, $"{peer.Name} isn't available", "They're offline or Holm is closed");
                return;
            }
            await using (channel)
            {
                ShowTask("", ShareBlue, $"{peer.Name}'s notch is open", "Waiting for them to accept…", null, null, ("Cancel", CancelTask));
                bool accepted = await FileTransfer.SendAsync(channel, _friends.MyName, paths,
                    new Progress<double>(p => ShowProgress($"Sending to {peer.Name}", Summary(paths), p, sending: true)), cts.Token, text);
                if (accepted) { EndTask("", GreenBrush, $"Sent to {peer.Name}", Summary(paths)); CountSent(paths); }
                else EndTask("", RedBrush, $"{peer.Name} declined", Summary(paths));
            }
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't send"); }
    }

    async Task SendWithCodeAsync(List<string> paths, bool text = false)
    {
        var cts = BeginTask();
        byte[] secret = ShareCode.NewSecret();
        string code = ShareCode.Format(secret);
        _ownCodes.Add(code);
        bool copied = await TrySetClipboardAsync($"Holm Beam code: {code}");
        ShowTask("", ShareBlue, copied ? "Code copied – paste it to your friend" : "Send this code to your friend", code, null, null, ("Cancel", CancelTask));
        try
        {
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), TimeSpan.FromMinutes(15), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
            ShowTask("", ShareBlue, "Your friend is connected", "Waiting for them to accept…", null, null, ("Cancel", CancelTask));
            bool accepted = await FileTransfer.SendAsync(channel, _friends.MyName, paths,
                new Progress<double>(p => ShowProgress("Sending", Summary(paths), p, sending: true)), cts.Token, text);
            if (accepted) { EndTask("", GreenBrush, "Sent", Summary(paths)); CountSent(paths); }
            else EndTask("", RedBrush, "Your friend declined", Summary(paths));
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, "The code expired", "No one used it within 15 minutes");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't send"); }
    }

    /// <summary>
    /// Like sending with a code, except that nobody has to pass the code on: it goes into the friend's inbox,
    /// sealed for them, and their notch comes to the relay by itself and asks them.
    /// </summary>
    async Task SendToFriendAsync(Friend friend, List<string> paths, bool text = false, QueuedSend? queued = null)
    {
        var cts = BeginTask();
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"Asking {friend.Name}…", Summary(paths), null, null, ("Cancel", CancelTask), textIcon: true);
        try
        {
            byte[] secret = ShareCode.NewSecret();
            await _friends.InviteAsync(friend, secret, cts.Token);
            ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"Checking if {friend.Name} is available…", "Waiting for their notch to answer", null, null, ("Cancel", CancelTask), textIcon: true);
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), FriendsService.SendPatience, cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: true);
            ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"{friend.Name}'s notch is open", "Waiting for them to accept…", null, null, ("Cancel", CancelTask), textIcon: true);
            bool accepted = await FileTransfer.SendAsync(channel, _friends.MyName, paths,
                new Progress<double>(p => ShowProgress($"Sending to {friend.Name}", Summary(paths), p, sending: true)), cts.Token, text);
            if (accepted) { EndTask("", GreenBrush, $"Sent to {friend.Name}", Summary(paths)); CountSent(paths); }
            else EndTask("", RedBrush, $"{friend.Name} declined", Summary(paths));
            if (queued != null) Unqueue(queued); // delivered, or they said no
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            if (queued != null)
            {
                // Still not there: keep waiting, without a word.
                _busy = false;
                _friends.MarkOffline(friend);
                HideTask();
            }
            else if (_settings.SendLater)
            {
                var send = Enqueue(friend, paths, text);
                EndTask(LaterGlyph, TimerOrange, $"Couldn't reach {friend.Name} right now", "It goes once you're both online",
                        ("Don't send", () => { HideTask(); Unqueue(send); }));
                _taskHide.Interval = TimeSpan.FromSeconds(8);
                _taskHide.Start();
            }
            else EndTask("", RedBrush, $"{friend.Name} isn't available", "They're offline or Holm is closed");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't send"); }
    }

    // ───────────── receiving ─────────────

    /// <summary>A friend left a note: they want to send something. Their notch waits on the relay.</summary>
    void OnFriendFiles(Friend friend, byte[] secret)
    {
        if (_busy || _hiddenForFullscreen || !_settings.Beam)
        {
            // Say no straight away, rather than leave them waiting.
            Diag.Log($"Declining {friend.Name}: busy");
            _ = DeclineAsync(secret);
            return;
        }
        _ = ReceiveFromFriendAsync(friend, secret);
    }

    async Task ReceiveFromFriendAsync(Friend friend, byte[] secret)
    {
        var cts = BeginTask();
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"{friend.Name} is sending you something", "Connecting…", null, null, ("Cancel", CancelTask), textIcon: true);
        try
        {
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectWithRetryAsync(host, port, ShareCode.RelayToken(secret), TimeSpan.FromMinutes(2), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            // The note proved who it is, so show the name we know them by, not the one their notch sends.
            await ReceiveIntoDownloadsAsync(channel, cts, knownSender: friend.Name);
        }
        catch (OperationCanceledException) when (!cts.IsCancellationRequested)
        {
            EndTask("", RedBrush, $"{friend.Name} is gone", "They cancelled or went offline");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't receive"); }
    }

    async Task DeclineAsync(byte[] secret)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            var (host, port) = RelayEndpoint();
            await using var stream = await RelayLink.ConnectAsync(host, port, ShareCode.RelayToken(secret), cts.Token);
            await using var channel = new SecureChannel(stream, ShareCode.Key(secret), initiator: false);
            await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(false), _shell.DownloadsFolder(), null, cts.Token);
        }
        catch (Exception ex) { Diag.Log("Couldn't decline: " + ex.Message); }
    }

    /// <summary>Someone added us as a friend: keep the offer on Add a friend and pop it out when possible.</summary>
    void OnFriendAddedUs(Friend card)
    {
        if (_friends.Find(card.Key) != null) return;
        _addBack.RemoveAll(f => f.Key == card.Key);
        _addBack.Add(card);
        _social?.Raise();
        if (_busy || _taskVisible || _hiddenForFullscreen)
        {
            ShowPeek("", $"{card.Name} added you", "", null);
            return;
        }
        ShowTask(FaceText(card.Name), FaceBrush(card.Name), $"{card.Name} added you as a friend", "Add them back to send each other files",
                 null, ("Add back", () => { HideTask(); _ = AddFriendAsync(card, tellThem: false); }), ("Not now", HideTask), textIcon: true);
        _taskHide.Interval = TimeSpan.FromSeconds(30);
        _taskHide.Start();
    }

    void OnFriendRequested(Friend card, string requestId)
    {
        if (_friends.Find(card.Key) != null) return;
        if (_friendRequests.Any(request => request.RequestId == requestId)) return;
        _friendRequests.Add((card, requestId));
        if (_busy || _taskVisible || _hiddenForFullscreen)
        {
            ShowPeek("", $"{card.Name} wants to be your friend", "", null, seconds: 4);
            return;
        }
        ShowFriendRequest(card, requestId);
    }

    void ShowFriendRequest(Friend card, string requestId)
    {
        _shownFriendRequestId = requestId;
        ShowTask(FaceText(card.Name), FaceBrush(card.Name), $"{card.Name} wants to be your friend", "Accept to add each other to your lists",
            null, ("Accept", () => _ = AnswerFriendRequestAsync(card, requestId, accepted: true)),
            ("Decline", () => _ = AnswerFriendRequestAsync(card, requestId, accepted: false)), textIcon: true);
        _taskHide.Interval = TimeSpan.FromSeconds(30);
        _taskHide.Start();
    }

    void OnFriendRequestCancelled(Friend card, string requestId)
    {
        _friendRequests.RemoveAll(request => request.RequestId == requestId);
        if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
        if (_shownFriendRequestId == requestId)
        {
            _shownFriendRequestId = null;
            HideTask();
        }
        ShowPeek("", $"{card.Name} cancelled the friend request", "", null, seconds: 4);
        if (_friendRequests.Count > 0 && !_taskVisible && !_busy)
            ShowFriendRequest(_friendRequests[0].Card, _friendRequests[0].RequestId);
    }

    void OnFriendRemovedUs(Friend friend)
    {
        if (_addBack.RemoveAll(f => f.Key == friend.Key) > 0) _social?.Raise();
        if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
        ShowPeek("", $"{friend.Name} removed you", "", null, seconds: 4);
    }

    async Task AddFriendAsync(Friend card, bool tellThem)
    {
        if (_addBack.RemoveAll(f => f.Key == card.Key) > 0) _social?.Raise();
        if (_friends.Find(card.Key) != null)
        {
            ShowPeek("", $"{card.Name} is already your friend", "", null, seconds: 4);
            return;
        }
        if (!tellThem)
        {
            await _friends.AddAsync(card, tellThem: false);
            ShowPeek("", $"{card.Name} is now a friend", "", null, seconds: 4);
            if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
            return;
        }

        var cts = BeginTask();
        ShowTask(FaceText(card.Name), FaceBrush(card.Name), $"Asking {card.Name}…", "Waiting for them to accept or decline",
            null, null, ("Cancel", CancelTask), textIcon: true);
        try
        {
            var result = await _friends.RequestAddAsync(card, TimeSpan.FromMinutes(2), cts.Token);
            if (result == FriendRequestResult.Accepted)
            {
                EndTask("", ShareBlue, $"You and {card.Name} are now friends", "You can send each other files");
                if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
            }
            else if (result == FriendRequestResult.Declined)
                EndTask("", RedBrush, $"{card.Name} declined", "They weren't added to your friends list");
            else
                EndTask("", ShareBlue, $"Request sent to {card.Name}", "You'll be friends as soon as they accept");
        }
        catch (Exception ex) { Fail(ex, cts, "Couldn't add friend"); }
    }

    async Task AnswerFriendRequestAsync(Friend card, string requestId, bool accepted)
    {
        HideTask();
        if (_shownFriendRequestId == requestId) _shownFriendRequestId = null;
        try
        {
            await _friends.RespondToFriendRequestAsync(card, requestId, accepted);
            _friendRequests.RemoveAll(request => request.RequestId == requestId);
            if (accepted && _friends.Find(card.Key) != null)
            {
                ShowPeek("", $"You and {card.Name} are now friends", "", null, seconds: 4);
                if (_sharing) { RefreshShareTargets(); ApplyState(animate: true); }
            }
            else if (!accepted) ShowPeek("", $"Declined {card.Name}'s request", "", null, seconds: 4);
        }
        catch (Exception ex)
        {
            Diag.Log($"Couldn't answer {card.Name}'s friend request: {ex.Message}");
            ShowPeek("", "Couldn't answer friend request", ex.Message, RedBrush, seconds: 5);
        }
        if (_friendRequests.Count > 0 && !_taskVisible && !_busy) ShowFriendRequest(_friendRequests[0].Card, _friendRequests[0].RequestId);
    }

    /// <summary>Someone on the network connected (worker thread). We answer on the UI thread; busy means no.</summary>
    Task OnNearbyIncoming(SecureChannel channel, string from)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_busy || _hiddenForFullscreen || !_settings.Beam)
            {
                await FileTransfer.ReceiveAsync(channel, _ => Task.FromResult(false), _shell.DownloadsFolder(), null, CancellationToken.None);
                return;
            }
            var cts = BeginTask();
            try { await ReceiveIntoDownloadsAsync(channel, cts); }
            catch (Exception ex) { Fail(ex, cts, "Couldn't receive"); }
        });

    /// <summary>A copied code: offer to fetch the file, or to add the friend. Our own codes and ones already asked about are skipped.</summary>
    async Task OnClipboardForBeamAsync()
    {
        if (!_settings.Beam || _busy || _taskVisible || _hiddenForFullscreen || Clipboard is not { } clipboard) return;
        try
        {
            if (await PrivacyOfClipboardAsync(clipboard) != ClipPrivacy.Keep) return; // a password: not ours to read
            string? text = await clipboard.TryGetTextAsync();
            if (OfferCopiedInvite(text)) return;
            if (FriendIdentity.ParseFriendCode(text) is { } card)
            {
                if (_friends.IsMe(card) || _friends.Find(card.Key) != null || card.Key == _lastOfferedFriend) return;
                _lastOfferedFriend = card.Key;
                ShowTask(FaceText(card.Name), FaceBrush(card.Name), $"Add {card.Name} as a friend?", "Then you can beam each other files, anywhere",
                         null, ("Add", () => { HideTask(); _ = AddFriendAsync(card, tellThem: true); }), ("Not now", HideTask), textIcon: true);
                _taskHide.Interval = TimeSpan.FromSeconds(20);
                _taskHide.Start();
                return;
            }
            if (!ShareCode.TryFind(text, out var secret, out var code)) return;
            if (_ownCodes.Contains(code) || code == _lastOfferedCode) return;
            _lastOfferedCode = code;
            ShowTask("", ShareBlue, "Holm code copied", "Get the file your friend is sending?", null,
                     ("Receive", () => _ = ReceiveWithCodeAsync(secret)), ("Not now", HideTask));
            _taskHide.Interval = TimeSpan.FromSeconds(20);
            _taskHide.Start();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Beam clipboard: " + ex.GetType().Name); }
    }

    async Task ReceiveWithCodeAsync(byte[] secret)
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, "Connecting…", "Waiting for the sender", null, null, ("Cancel", CancelTask));
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

    async Task ReceiveIntoDownloadsAsync(SecureChannel channel, CancellationTokenSource cts, string? knownSender = null)
    {
        string sender = "";
        IReadOnlyList<string>? saved;
        try
        {
            // The transfer continues on worker threads, so the question has to be asked on the UI thread.
            saved = await FileTransfer.ReceiveAsync(channel,
                (offer, decisionToken) => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (knownSender != null) offer = offer with { Sender = knownSender };
                    sender = offer.Sender;
                    return AskAsync(offer, decisionToken);
                }),
                offer => offer.IsText && _settings.BeamText ? TextInbox : _shell.DownloadsFolder(),
                new Progress<double>(p => ShowProgress($"Receiving from {sender}", "Saving to Downloads", p, sending: false)), cts.Token);
        }
        catch (FileTransfer.SenderCancelledException)
        {
            EndTask("", RedBrush, "Sender cancelled", "They cancelled before you responded");
            return;
        }
        if (saved != null && saved.Count == 1 && saved[0].StartsWith(TextInbox, StringComparison.OrdinalIgnoreCase))
        {
            await TakeBeamedTextAsync(saved[0], sender);
            return;
        }
        if (saved == null) { EndTask("", RedBrush, "Declined", $"Nothing was saved from {sender}"); return; }
        string first = saved[0];
        // As a finished browser download: open the file, or show it in its folder.
        EndTask("", GreenBrush, saved.Count == 1 ? $"Saved {Path.GetFileName(first)}" : $"Saved {saved.Count} files",
                $"From {sender} · in Downloads",
                ("Open", () => { HideTask(); _shell.Open(first); }),
                ("Show", () => { HideTask(); _shell.Reveal(first); }));
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    /// <summary>The question in the notch. No answer within a minute counts as no.</summary>
    async Task<bool> AskAsync(ShareOffer offer, CancellationToken ct)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool text = offer.IsText && _settings.BeamText;
        ShowTask(FaceText(offer.Sender), FaceBrush(offer.Sender), text ? $"Text from {offer.Sender}" : $"{offer.Sender} wants to send you",
                 text ? "Copy it to your clipboard?" : Summary(offer), null,
                 (text ? "Copy" : "Accept", () => { _acceptTick = Environment.TickCount64; answer.TrySetResult(true); }), ("Decline", () => answer.TrySetResult(false)), textIcon: true);
        PlayIncomingFileSound();
        using (ct.Register(() => answer.TrySetResult(false)))
        {
            var finished = await Task.WhenAny(answer.Task, Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None));
            bool accepted = finished == answer.Task && answer.Task.Result;
            if (accepted) ShowTask("", ShareBlue, $"Receiving from {offer.Sender}", text ? "Copying it" : "Saving to Downloads", 0, null, ("Cancel", CancelTask));
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

    // Ignore the second click of a double-click on Accept, which lands on the Cancel that replaces it.
    void CancelTask() { if (Environment.TickCount64 - _acceptTick > 600) _taskCts?.Cancel(); }

    /// <summary>Progress, at most a few times a second (every 64 KB would be far too many redraws).</summary>
    void ShowProgress(string title, string subtitle, double fraction, bool sending)
    {
        if (!_busy) return;
        var now = DateTime.UtcNow;
        if (fraction > 0 && fraction < 1 && now - _lastProgressUi < TimeSpan.FromMilliseconds(150)) return;
        _lastProgressUi = now;
        ShowTask(sending ? "" : "", ShareBlue, title, $"{subtitle} · {Math.Round(fraction * 100)}%", fraction, null, ("Cancel", CancelTask));
    }

    void EndTask(string icon, IBrush circle, string title, string subtitle,
                 (string Label, Action Act)? primary = null, (string Label, Action Act)? secondary = null)
    {
        _busy = false;
        ShowTask(icon, circle, title, subtitle, null, primary, secondary ?? ("OK", HideTask));
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
            IOException => "The connection dropped – try again.",
            _ => "Something went wrong",
        };
        EndTask("", RedBrush, title, reason);
    }

    // ───────────── menu ─────────────

    async Task ReceiveCopiedCodeAsync()
    {
        string? text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (!ShareCode.TryFind(text, out var secret, out var code))
        {
            ShowPeek("", "Copy your friend's Holm code first", "", null);
            return;
        }
        if (_ownCodes.Contains(code))
        {
            ShowPeek("", "That's your own code – send it to a friend", "", null);
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

    async Task<bool> TrySetClipboardAsync(string text)
    {
        if (Clipboard is not { } clipboard) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { await clipboard.SetTextAsync(text); return true; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { await Task.Delay(30); } // another app has it open
        }
        return false;
    }
}
