using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Beaming text (the WPF app's MainWindow.BeamText.cs): what you copied goes to a friend, someone nearby or a code,
/// and lands on their clipboard with a note. It travels as one small text file marked as text. And sending later
/// (MainWindow.SendLater.cs): when a friend's Holm isn't running, what you dropped on them waits, across restarts, and
/// goes as soon as their Holm says it's there.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly string TextInbox = Path.Combine(Path.GetTempPath(), "Holm", "Text") + Path.DirectorySeparatorChar;
    const string LaterGlyph = ""; // a clock
    string? _pickText; // the text file the share view was opened for
    readonly DispatcherTimer _laterTick = new(), _laterSoon = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly HashSet<string> _laterSending = new();
    readonly StackPanel _laterRows = new();

    /// <summary>Beams what is on the clipboard now: the share view opens to click who gets it.</summary>
    async Task BeamCopiedTextAsync()
    {
        if (Clipboard is not { } clipboard) return;
        try
        {
            if (await PrivacyOfClipboardAsync(clipboard) != ClipPrivacy.Keep)
            {
                ShowPeek(null, "That's private (a password?) – not beamed", "", null);
                return;
            }
            string? text = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { ShowPeek(null, "Copy some text first", "", null); return; }
            BeamText(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Beam text: " + ex.GetType().Name); }
    }

    void BeamText(string text)
    {
        if (!_settings.Beam || _busy || _sharing) return;
        if (Encoding.UTF8.GetByteCount(text) > FileTransfer.MaxTextBytes)
        {
            ShowPeek(null, "That text is too long to beam", "Send it as a file", Dim);
            return;
        }
        string path;
        try
        {
            Directory.CreateDirectory(FolderPack.TempFolder);
            path = Path.Combine(FolderPack.TempFolder, "Beamed text.txt");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(FolderPack.TempFolder, $"Beamed text ({n}).txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Beam text: " + ex.GetType().Name); // never the text
            return;
        }
        ForgetPickedText();
        PickTarget(new[] { path });
        _pickText = path;
        RefreshShareTargets();
    }

    /// <summary>Sends the text file, then deletes it.</summary>
    async Task BeamTextFileAsync(ShareTarget target, string path)
    {
        if (target.Place == ShareTarget.PhonePlace)
        {
            BeamToPhone(new() { path });
            _phoneZips.Add(path);
            return;
        }
        try { await SendFilesAsync(target, new() { path }, text: true); }
        finally { FolderPack.Forget(new[] { path }, FolderPack.TempFolder); }
    }

    void ForgetPickedText()
    {
        if (_pickText is not { } path) return;
        _pickText = null;
        FolderPack.Forget(new[] { path }, FolderPack.TempFolder);
    }

    /// <summary>A text arrived: onto the clipboard it goes, and the file is gone. Busy clipboard: it stays in Downloads.</summary>
    async Task TakeBeamedTextAsync(string path, string sender)
    {
        string text;
        try { text = await File.ReadAllTextAsync(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Beamed text: " + ex.GetType().Name);
            EndTask("", RedBrush, "Couldn't read the text", "From " + sender);
            return;
        }
        if (await TrySetClipboardAsync(text))
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            EndTask("", GreenBrush, $"Text from {sender} copied", "Paste it with Ctrl+V");
            return;
        }
        // The clipboard is busy: keep it where they'd look for a file.
        string kept = Path.Combine(_shell.DownloadsFolder(), Path.GetFileName(path));
        try { File.Move(path, kept, overwrite: false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { kept = path; }
        EndTask("", GreenBrush, $"Text from {sender} saved", "The clipboard was busy · in Downloads",
                ("Open", () => { HideTask(); _shell.Open(kept); }),
                ("Show", () => { HideTask(); _shell.Reveal(kept); }));
    }

    /// <summary>Beam text › Beam copied text…, and whether beamed text goes to the clipboard.</summary>
    void FillBeamTextMenu(MenuItem menu)
    {
        menu.Items.Clear();
        menu.IsEnabled = _settings.Beam;
        var beam = new MenuItem { Header = "Beam copied text…", IsEnabled = _settings.BeamText };
        beam.Click += (_, _) => _ = BeamCopiedTextAsync();
        menu.Items.Add(beam);
        var receive = new MenuItem { Header = "Beamed text goes to the clipboard", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.BeamText };
        receive.Click += (_, _) => { _settings.BeamText = !_settings.BeamText; _settings.Save(); };
        menu.Items.Add(receive);
    }

    // ───────────── send later ─────────────

    void InitSendLater()
    {
        _laterTick.Tick += (_, _) => { _laterTick.Stop(); LookForWaitingFriends(); };
        _laterSoon.Tick += (_, _) => { _laterSoon.Stop(); SendWaitingToOnlineFriends(); }; // once the notch is free again
        _friends.FriendOnline += friend => { if (_settings.SendLater && Queued(friend).Any()) TrySendQueued(friend); };
    }

    void StartSendLater()
    {
        ForgetOrphanedLater();
        ShowLaterRows();
        PlanLaterLook();
    }

    IEnumerable<QueuedSend> Queued(Friend friend) => _settings.SendQueue.Where(q => q.FriendKey == friend.Key);

    /// <summary>The friend couldn't be reached: keep the files for later. Files Holm made move out of the way of cleanup.</summary>
    QueuedSend Enqueue(Friend friend, List<string> paths, bool text)
    {
        var send = new QueuedSend { FriendKey = friend.Key, FriendName = friend.Name, Text = text, QueuedUtc = DateTime.UtcNow, AskedUtc = DateTime.UtcNow };
        string own = Path.Combine(SendLater.Folder, send.Id);
        foreach (var path in paths)
        {
            if (!SendLater.IsHolmsOwn(path, FolderPack.TempFolder)) { send.Paths.Add(path); continue; }
            try
            {
                Directory.CreateDirectory(own);
                string moved = Path.Combine(own, Path.GetFileName(path));
                File.Move(path, moved);
                send.Paths.Add(moved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Log("Send later: " + ex.GetType().Name); }
        }
        _settings.SendQueue.Add(send);
        _settings.Save();
        _friends.MarkOffline(friend);
        Diag.Log("Send later: queued");
        ShowLaterRows();
        PlanLaterLook();
        return send;
    }

    void Unqueue(QueuedSend send)
    {
        _settings.SendQueue.RemoveAll(q => q.Id == send.Id);
        _settings.Save();
        try
        {
            string own = Path.Combine(SendLater.Folder, send.Id);
            if (Directory.Exists(own)) Directory.Delete(own, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        ShowLaterRows();
        PlanLaterLook();
    }

    /// <summary>Files Holm kept for sends that are no longer queued (cancelled while Holm was closing, say).</summary>
    void ForgetOrphanedLater()
    {
        try
        {
            if (!Directory.Exists(SendLater.Folder)) return;
            var ids = _settings.SendQueue.Select(q => q.Id).ToHashSet();
            foreach (var dir in Directory.GetDirectories(SendLater.Folder))
                if (!ids.Contains(Path.GetFileName(dir))) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void PlanLaterLook()
    {
        _laterTick.Stop();
        if (!_settings.SendLater || SendLater.NextLook(_settings.SendQueue, DateTime.UtcNow) is not { } next) return;
        _laterTick.Interval = TimeSpan.FromMilliseconds(Math.Clamp(next.TotalMilliseconds, 5_000, 3_600_000));
        _laterTick.Start();
    }

    /// <summary>Asks waiting friends whether they're here (and leaves the "something waits" note) as the plan says.</summary>
    void LookForWaitingFriends()
    {
        var now = DateTime.UtcNow;
        foreach (var send in _settings.SendQueue.ToList())
        {
            var friend = _friends.Find(send.FriendKey);
            if (friend == null || SendLater.Expired(send, now))
            {
                Unqueue(send);
                ShowPeek(LaterGlyph, friend == null ? $"Not sent: {send.FriendName} is no longer a friend" : $"Couldn't reach {send.FriendName} within a week", "", null, seconds: 5);
                continue;
            }
            if (SendLater.AskDue(send, now)) { _friends.AskIfHere(friend); send.AskedUtc = now; }
            if (SendLater.NoteDue(send, now)) { _friends.TellWaiting(friend); send.NotedUtc = now; }
        }
        _settings.Save();
        PlanLaterLook();
    }

    void SendWaitingToOnlineFriends()
    {
        foreach (var key in _settings.SendQueue.Select(q => q.FriendKey).Distinct().ToList())
            if (_friends.Find(key) is { } friend && _friends.IsOnline(friend)) { TrySendQueued(friend); return; }
    }

    async void TrySendQueued(Friend friend)
    {
        if (Queued(friend).FirstOrDefault() is not { } send || _laterSending.Contains(send.Id)) return;
        if (_busy || _taskVisible || _sharing || _hiddenForFullscreen || !_settings.Beam)
        {
            _laterSoon.Stop();
            _laterSoon.Start();
            return;
        }
        var paths = send.Paths.Where(File.Exists).ToList();
        if (paths.Count == 0)
        {
            Unqueue(send);
            ShowPeek(LaterGlyph, $"Not sent to {friend.Name}: the files are gone", "", null, seconds: 5);
            return;
        }
        _laterSending.Add(send.Id);
        try { await SendToFriendAsync(friend, paths, send.Text, send); }
        finally { _laterSending.Remove(send.Id); }
        if (Queued(friend).Any()) { _laterSoon.Stop(); _laterSoon.Start(); } // the next one for them
    }

    /// <summary>What waits, in the open notch, each with "Don't send".</summary>
    void ShowLaterRows()
    {
        _laterRows.Children.Clear();
        foreach (var send in _settings.SendQueue)
        {
            var row = new Grid { Margin = new Thickness(2, 12, 2, 0), ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(new TextBlock { Text = LaterGlyph, FontFamily = IconFont, FontSize = 14, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"For {send.FriendName}: {SendLater.Describe(send)}", FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = _settings.SendLater ? "Goes when their Holm is back" : "Paused (Send later is off)", FontSize = 11, Foreground = Dim });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var cancel = NotchButton.Pill();
            cancel.Label = "Don't send";
            cancel.VerticalAlignment = VerticalAlignment.Center;
            var which = send;
            cancel.Click += () => Unqueue(which);
            Grid.SetColumn(cancel, 2);
            row.Children.Add(cancel);
            _laterRows.Children.Add(row);
        }
        if (_hovered && _state == NotchState.Expanded) ApplyState(animate: true);
    }

    void SetSendLater(bool on)
    {
        _settings.SendLater = on;
        _settings.Save();
        ShowLaterRows();
        PlanLaterLook();
        if (on) SendWaitingToOnlineFriends();
    }
}
