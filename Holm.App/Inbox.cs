using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;
using WinNotch.Notify;

namespace Holm.App;

/// <summary>
/// The WPF app's MessagesService without its reading (which is the backend's): which messages are unread. A
/// notification counts as unread until you look at its app or click it in the notch; a messenger's icon badge adds
/// the messages it has no notification for, and its rising number pops out as "1 unread message" when no
/// notification says more. Nothing is ever removed or changed in the system.
/// </summary>
internal sealed class Inbox
{
    const int SavedReadLimit = 256;
    readonly Func<string, bool> _shown; // is this messenger switched on?
    readonly List<ReadToast>? _savedRead;
    readonly Action? _saveRead;
    readonly DispatcherTimer _badgePop = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    readonly Dictionary<uint, ChatMessage?> _seen = new(); // null: a notification from another app
    readonly HashSet<uint> _read = new();
    readonly Dictionary<string, string> _appIds = new();
    readonly Dictionary<string, BadgeCounter> _counts = new();
    readonly Dictionary<string, DateTimeOffset> _countTimes = new();
    readonly Dictionary<string, string> _badgeRevisions = new();
    readonly HashSet<string> _pendingBadgePops = new();
    List<ChatMessage> _toasts = new(); // unread notifications, oldest first
    List<ChatMessage> _messages = new();
    DateTimeOffset _watchingSince = DateTimeOffset.MaxValue;

    /// <summary>Unread messages of the messengers that are switched on, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public int UnreadCount => _messages.Sum(m => m.Count);

    /// <summary>The list changed. The argument is the message that just arrived, or null if none did.</summary>
    public event Action<ChatMessage?>? Changed;

    /// <summary>Also report notifications of every other app (<see cref="OtherArrived"/>), not only messengers.</summary>
    public bool EveryApp { get; set; }

    /// <summary>A notification of an app that isn't a messenger arrived (with <see cref="EveryApp"/> on): the newest of a burst.</summary>
    public event Action<ChatMessage>? OtherArrived;

    public Inbox(Func<string, bool> shown, List<ReadToast>? savedRead = null, Action? saveRead = null)
    {
        _shown = shown;
        _savedRead = savedRead;
        _saveRead = saveRead;
        // An app's number went up: wait a moment for its notification, which says who wrote what.
        _badgePop.Tick += (_, _) => { _badgePop.Stop(); Publish(null, popBadgeCount: true); };
    }

    public void StartWatching(DateTimeOffset time) => _watchingSince = time;

    /// <summary>The app id the system reported for a messenger's notifications or badge, for its logo and to open it.</summary>
    public string? AppIdOf(string app) => _appIds.TryGetValue(app, out var id) ? id : null;

    /// <summary>Takes one look at the system's notifications and badges.</summary>
    public void Apply(NotificationSnapshot snapshot)
    {
        ChatMessage? arrived = null;
        if (snapshot.Toasts is { } toasts) arrived = ReadToasts(toasts);
        if (snapshot.Badges is { } badges && Messengers.All.Any(app => _shown(app.Key))) ReadBadges(badges, arrived, snapshot.Revisions);
        Publish(arrived);
    }

    ChatMessage? ReadToasts(IReadOnlyList<SystemNotification> all)
    {
        var unread = new List<ChatMessage>();
        var present = new HashSet<uint>();
        ChatMessage? arrived = null, other = null;
        foreach (var notification in all)
        {
            uint id = notification.Id;
            present.Add(id);
            string? appId = notification.AppId;
            var latest = Messengers.Parse(appId, string.IsNullOrEmpty(appId) ? notification.AppName : null, notification.Lines.ToList<string?>(), id, notification.Time);
            bool live = notification.Time >= _watchingSince;
            if (latest == null && EveryApp && live && !_seen.ContainsKey(id)
                && Pop(appId, notification.AppName, notification.Lines, id, notification.Time) is { } any
                && (other == null || any.Time >= other.Time))
                other = any;
            bool seen = _seen.TryGetValue(id, out var message);
            if (!seen || latest?.App != message?.App
                || latest?.Time != message?.Time || latest?.Title != message?.Title || latest?.Text != message?.Text)
            {
                _seen[id] = message = latest;
                _read.Remove(id); // the system can replace an existing notification using the same id
                if (message != null)
                {
                    if (appId != null && message.Site == null) _appIds[message.App] = appId; // a browser's id doesn't open the app
                    // The notification center may arrive after one or more badge-only snapshots.
                    if (!seen && _savedRead?.Contains(ReadToast.Of(message)) == true) _read.Add(id);
                    else if (live && _shown(message.App) && (arrived == null || message.Time >= arrived.Time)) arrived = message;
                }
            }
            if (message != null && !_read.Contains(id)) unread.Add(message);
        }
        foreach (var gone in _seen.Keys.Where(id => !present.Contains(id)).ToList()) _seen.Remove(gone);
        _read.IntersectWith(present);
        // The system can expire a notification while its message is still unread: keep it until the messenger is
        // opened or its row is clicked.
        unread.AddRange(_toasts.Where(m => !present.Contains(m.Id) && !_read.Contains(m.Id)));
        unread.RemoveAll(m => _read.Contains(m.Id));
        unread.Sort((a, b) => a.Time.CompareTo(b.Time));
        _toasts = unread;
        if (other != null) OtherArrived?.Invoke(other);
        return arrived;
    }

    void ReadBadges(IReadOnlyDictionary<string, string> badges, ChatMessage? arrived, IReadOnlyDictionary<string, string>? revisions)
    {
        foreach (var app in Messengers.All.Where(app => _shown(app.Key)))
        {
            int? count = NotificationBadges.CountFor(badges, app);
            if (count == null)
            {
                // A badge row can go when an app clears its count. A glyph carries no number: keep waiting for one.
                bool hasBadge = badges.Keys.Any(id => Messengers.ForApp(id)?.Key == app.Key);
                if (hasBadge || !_counts.ContainsKey(app.Key)) continue;
                count = 0;
            }
            if (!_appIds.ContainsKey(app.Key) && badges.Keys.FirstOrDefault(key => Messengers.ForApp(key)?.Key == app.Key) is { } badgeId)
                _appIds[app.Key] = badgeId;
            if (!_counts.TryGetValue(app.Key, out var counter)) _counts[app.Key] = counter = new BadgeCounter();
            int? before = counter.Count;
            bool first = before == null;
            string revision = revisions == null ? "" : string.Join("|", revisions
                .Where(pair => Messengers.ForApp(pair.Key)?.Key == app.Key)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key + ":" + pair.Value));
            bool refreshed = revision.Length > 0 && _badgeRevisions.TryGetValue(app.Key, out var previous) && previous != revision;
            if (revision.Length > 0) _badgeRevisions[app.Key] = revision;
            else if (count == 0) _badgeRevisions.Remove(app.Key);
            bool up = counter.Update(count.Value, refreshed);
            if (first || up) _countTimes[app.Key] = DateTimeOffset.Now;
            if (before != count) Diag.Log($"{app.Name} badge: {count}");
            if (before > 0 && count == 0)
            {
                MarkToastsRead(app.Key);
                _pendingBadgePops.Remove(app.Key);
            }
            if (arrived?.App == app.Key) _pendingBadgePops.Remove(app.Key);
            else if (up)
            {
                _pendingBadgePops.Add(app.Key);
                _badgePop.Stop();
                _badgePop.Start();
            }
        }
    }

    /// <summary>You've seen them (the app is in front, or you clicked them): clears that app's messages, or all with null.</summary>
    public void MarkRead(string? app = null)
    {
        bool any = MarkToastsRead(app);
        foreach (var (key, counter) in _counts.Where(p => app == null || p.Key == app))
        {
            if (counter.Unread > 0) { counter.MarkSeen(); any = true; }
            _pendingBadgePops.Remove(key);
        }
        if (any) Publish(null);
    }

    bool MarkToastsRead(string? app)
    {
        bool any = false, save = false;
        foreach (var message in _toasts.Where(m => app == null || m.App == app))
        {
            _read.Add(message.Id);
            any = true;
            if (_savedRead is not { } saved) continue;
            var key = ReadToast.Of(message);
            if (saved.Contains(key)) continue;
            saved.Add(key);
            save = true;
        }
        _toasts.RemoveAll(m => _read.Contains(m.Id));
        if (save && _savedRead is { } history)
        {
            if (history.Count > SavedReadLimit) history.RemoveRange(0, history.Count - SavedReadLimit);
            _saveRead?.Invoke();
        }
        return any;
    }

    /// <summary>A messenger was switched on or off.</summary>
    public void Refilter() => Publish(null);

    /// <summary>Watching stopped: forget everything.</summary>
    public void Reset()
    {
        _badgePop.Stop();
        _watchingSince = DateTimeOffset.MaxValue;
        _seen.Clear();
        _read.Clear();
        _toasts = new();
        _counts.Clear();
        _countTimes.Clear();
        _badgeRevisions.Clear();
        _pendingBadgePops.Clear();
        if (_messages.Count == 0) return;
        _messages = new();
        Changed?.Invoke(null);
    }

    /// <summary>Builds the list from unread notifications and numeric badges, and tells the window.</summary>
    void Publish(ChatMessage? arrived, bool popBadgeCount = false)
    {
        var list = _toasts.Where(m => _shown(m.App)).ToList();
        var countMessages = new List<ChatMessage>();
        foreach (var app in Messengers.All.Where(app => _shown(app.Key)))
        {
            int fromBadge = _counts.TryGetValue(app.Key, out var counter) ? counter.Unread : 0;
            int withText = list.Count(m => m.App == app.Key);
            if (fromBadge > withText)
            {
                var countOnly = new ChatMessage
                {
                    App = app.Key, Title = app.Name,
                    CountOnly = true, Count = fromBadge - withText, Time = _countTimes[app.Key],
                    Text = fromBadge == 1 ? "1 unread message" : $"{fromBadge} unread messages",
                };
                countMessages.Add(countOnly);
                list.Add(countOnly);
            }
        }
        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        if (popBadgeCount)
        {
            arrived = countMessages.Where(m => _pendingBadgePops.Contains(m.App)).OrderBy(m => m.Time).LastOrDefault();
            _pendingBadgePops.Clear();
        }

        bool changed = arrived != null || list.Count != _messages.Count
                    || !list.Zip(_messages).All(p => p.First.Id == p.Second.Id && p.First.App == p.Second.App
                        && p.First.Count == p.Second.Count && p.First.CountOnly == p.Second.CountOnly);
        _messages = list;
        if (changed) Changed?.Invoke(arrived);
    }

    /// <summary>The pop-out for any app's notification: its first line as the title, the rest as the text.</summary>
    internal static ChatMessage? Pop(string? appId, string? name, IReadOnlyList<string> lines, uint id, DateTimeOffset time)
    {
        var words = lines.Select(l => (l ?? "").Trim()).Where(l => l.Length > 0).ToList();
        string app = !string.IsNullOrWhiteSpace(appId) ? appId! : name ?? "";
        if (app.Length == 0 || words.Count == 0 && string.IsNullOrWhiteSpace(name)) return null;
        string title = words.Count > 0 ? words[0] : name!;
        string text = string.Join(" · ", words.Skip(1));
        return new ChatMessage { Id = id, App = app, Title = title, Text = text.Length > 200 ? text[..200] + "…" : text, Time = time };
    }
}
