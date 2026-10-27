using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Sending later: when a friend's Holm isn't running, what you dropped on them waits (across restarts) and goes as
/// soon as their Holm says it's there (FriendsService.FriendOnline). "Don't send" takes it back.
/// </summary>
public partial class MainWindow
{
    const string LaterGlyph = "\uE823"; // a clock
    DispatcherTimer _laterTick = null!, _laterSoon = null!;
    readonly HashSet<string> _laterSending = new();

    void InitSendLater()
    {
        _laterTick = MakeTimer(60_000, LookForWaitingFriends, oneShot: true);
        _laterSoon = MakeTimer(30_000, SendWaitingToOnlineFriends, oneShot: true); // once the notch is free again
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Diag.Log("Send later: " + ex.GetType().Name);
            }
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
                ShowPeek(LaterGlyph, friend == null ? $"Not sent: {send.FriendName} is no longer a friend" : $"Couldn't reach {send.FriendName} within a week",
                         "", null, false, null, seconds: 5);
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
        if (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen || !_settings.Beam)
        {
            _laterSoon.Stop();
            _laterSoon.Start();
            return;
        }
        var paths = send.Paths.Where(File.Exists).ToList();
        if (paths.Count == 0)
        {
            Unqueue(send);
            ShowPeek(LaterGlyph, $"Not sent to {friend.Name}: the files are gone", "", null, false, null, seconds: 5);
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
        LaterRows.Children.Clear();
        foreach (var send in _settings.SendQueue)
        {
            var row = new Grid { Margin = new Thickness(2, 12, 2, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = LaterGlyph, FontFamily = IconFont, FontSize = 14, Foreground = TimerOrange, VerticalAlignment = VerticalAlignment.Center });
            var text = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = $"For {send.FriendName}: {SendLater.Describe(send)}", FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = _settings.SendLater ? "Goes when their Holm is back" : "Paused (Send later is off)", FontSize = 11, Foreground = DimBrush });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var cancel = new Button { Style = (Style)FindResource("PillButton"), Content = "Don't send", VerticalAlignment = VerticalAlignment.Center };
            var which = send;
            cancel.Click += (_, _) => Unqueue(which);
            Grid.SetColumn(cancel, 2);
            row.Children.Add(cancel);
            LaterRows.Children.Add(row);
        }
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    void MenuSendLater_Click(object sender, RoutedEventArgs e)
    {
        _settings.SendLater = MenuSendLater.IsChecked;
        _settings.Save();
        ShowLaterRows();
        PlanLaterLook();
        if (_settings.SendLater) SendWaitingToOnlineFriends();
    }
}
