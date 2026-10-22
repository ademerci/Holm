using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch.Notify;

namespace WinNotch;

/// <summary>
/// Messages from chat and mail apps (WhatsApp, Discord, Outlook, …), read from the Windows notification
/// center: a notification counts as unread until you look at its app or click it in the notch. That needs
/// "notification access" (Settings › Privacy › Notifications). Nothing is ever removed or changed.
/// </summary>
/// <remarks>
/// Some apps show their own pop-ups or leave no toast in the notification center, so we also read each
/// supported app's numeric taskbar badge from the notification database; this needs no listener access.
/// Windows only sends change events to packaged apps, so instead we watch the database's folder,
/// which Windows writes whenever a notification or badge changes, and only then look. A short timer is the
/// safety net because Windows can omit file watcher events while updating its notification database.
/// </remarks>
internal sealed class MessagesService
{
    public enum AccessState { Off, Allowed, Denied, Unavailable }

    readonly Func<string, bool> _shown; // is this messenger switched on?
    readonly DispatcherTimer _poll, _settle, _badgePop;
    FileSystemWatcher? _watcher;
    readonly Dictionary<uint, ChatMessage?> _seen = new(); // null: a notification from another app
    readonly HashSet<uint> _read = new();
    readonly Dictionary<string, string> _appIds = new();
    readonly Dictionary<string, ImageSource> _logos = new();
    readonly HashSet<string> _logoTried = new();
    UserNotificationListener? _listener;
    bool _running, _evented, _primed, _busy, _again, _starting;
    int _generation; // bumped by Stop, so a start still waiting for Windows' answer gives up
    List<ChatMessage> _toasts = new(); // unread notifications, oldest first
    List<ChatMessage> _messages = new();
    readonly Dictionary<string, BadgeCounter> _counts = new();
    readonly Dictionary<string, DateTimeOffset> _countTimes = new();
    readonly Dictionary<string, string> _badgeRevisions = new();
    readonly HashSet<string> _pendingBadgePops = new();

    public AccessState Access { get; private set; } = AccessState.Off;
    /// <summary>Unread messages of the messengers that are switched on, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public int UnreadCount => _messages.Sum(m => m.Count);

    /// <summary>The list changed. The argument is the message that just arrived, or null if none did.</summary>
    public event Action<ChatMessage?>? Changed;

    public MessagesService(Dispatcher ui, Func<string, bool> shown)
    {
        _shown = shown;
        _poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(60) };
        _poll.Tick += (_, _) => _ = RefreshAsync();
        // A notification arriving means a burst of database writes: look once, shortly after the first.
        _settle = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(700) };
        _settle.Tick += (_, _) => { _settle.Stop(); _ = RefreshAsync(); };
        // An app's number went up: wait a moment for its notification, which says who wrote what.
        _badgePop = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(1500) };
        _badgePop.Tick += (_, _) => { _badgePop.Stop(); Publish(null, popBadgeCount: true); };
    }

    public bool IsRunning => _running;

    /// <summary>The app id Windows reported for a messenger's notifications or badge, to open it.</summary>
    public string? AppIdOf(string app) => _appIds.TryGetValue(app, out var id) ? id : null;

    /// <summary>The messenger's own icon, once a notification brought it along or its badge told us where it lives.</summary>
    public ImageSource? LogoOf(string app) => _logos.TryGetValue(app, out var logo) ? logo : null;

    /// <summary>
    /// Starts watching. With notification access, every messenger's notifications; numeric badges work without.
    /// <paramref name="ask"/>: request access, which may show Windows' consent prompt; otherwise only check,
    /// so hovering the notch never pops up a dialog.
    /// </summary>
    public async Task StartAsync(bool ask)
    {
        if (_starting || (_running && _listener != null)) return;
        _starting = true;
        int generation = _generation;
        try
        {
            if (!_running)
            {
                _running = true;
                WatchDatabase();
                // Windows sometimes updates the notification database without delivering a reliable
                // FileSystemWatcher event. Keep a short fallback so toasts still appear promptly.
                _poll.Interval = TimeSpan.FromSeconds(5);
                _poll.Start();
            }
            try
            {
                var listener = UserNotificationListener.Current;
                var access = ask ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
                if (generation != _generation) return;
                Access = access == UserNotificationListenerAccessStatus.Allowed ? AccessState.Allowed : AccessState.Denied;
                Diag.Log("Notification access: " + access);
                if (Access == AccessState.Allowed)
                {
                    _listener = listener;
                    try
                    {
                        listener.NotificationChanged += OnNotificationChanged;
                        _evented = true;
                    }
                    catch (Exception ex) { Diag.Log("Notification events unavailable: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                Access = AccessState.Unavailable;
                Diag.Log("Notification listener unavailable: " + ex.Message);
            }
        }
        finally { _starting = false; }
        if (generation == _generation) await RefreshAsync();
    }

    bool WatchDatabase()
    {
        try
        {
            string folder = NotificationBadges.DefaultFolder;
            if (!Directory.Exists(folder)) return false;
            _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            FileSystemEventHandler changed = (_, _) => _settle.Dispatcher.InvokeAsync(() => { if (!_settle.IsEnabled) _settle.Start(); });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.EnableRaisingEvents = true;
            Diag.Log("Watching the notification database");
            return true;
        }
        catch (Exception ex)
        {
            Diag.Log("Notification database can't be watched, looking every 10 s: " + ex.Message);
            _watcher?.Dispose();
            _watcher = null;
            return false;
        }
    }

    public void Stop()
    {
        _generation++;
        _running = false;
        _poll.Stop();
        _settle.Stop();
        _badgePop.Stop();
        _watcher?.Dispose();
        _watcher = null;
        if (_listener != null && _evented)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
        _listener = null;
        _evented = _primed = false;
        Access = AccessState.Off;
        _seen.Clear();
        _read.Clear();
        _toasts = new();
        _counts.Clear();
        _countTimes.Clear();
        _badgeRevisions.Clear();
        _pendingBadgePops.Clear();
        if (_messages.Count > 0)
        {
            _messages = new();
            Changed?.Invoke(null);
        }
    }

    void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        => _poll.Dispatcher.InvokeAsync(() => _ = RefreshAsync());

    /// <summary>You've seen them (the app is in front, or you clicked them): clears that app's messages, or all with null.</summary>
    public void MarkRead(string? app = null)
    {
        bool any = false;
        foreach (var message in _toasts.Where(m => app == null || m.App == app))
        {
            _read.Add(message.Id);
            any = true;
        }
        _toasts.RemoveAll(m => _read.Contains(m.Id));
        foreach (var (key, counter) in _counts.Where(p => app == null || p.Key == app))
        {
            if (counter.Unread > 0) { counter.MarkSeen(); any = true; }
            _pendingBadgePops.Remove(key);
        }
        if (any) Publish(null);
    }

    /// <summary>A messenger was switched on or off in the menu.</summary>
    public void Refilter()
    {
        if (!_running) return;
        Publish(null);
        _ = RefreshAsync();
    }

    async Task RefreshAsync()
    {
        if (!_running) return;
        if (_busy) { _again = true; return; }
        _busy = true;
        try
        {
            do
            {
                _again = false;
                await RefreshCoreAsync();
            } while (_again && _running);
        }
        catch (Exception ex) { Diag.Log("Reading notifications failed: " + ex.Message); }
        finally { _busy = false; }
    }

    async Task RefreshCoreAsync()
    {
        int generation = _generation;
        ChatMessage? arrived = null;
        if (_listener is { } listener)
        {
            try
            {
                // Access can be revoked while running. Windows may return an empty list instead of throwing.
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    Access = AccessState.Allowed;
                    arrived = await ReadToastsAsync(listener);
                }
                else
                {
                    Access = AccessState.Denied;
                    _toasts.Clear();
                }
            }
            catch (Exception ex)
            {
                // A broken listener must not prevent the independent numeric badge fallback from running.
                Diag.Log("Reading notification toasts failed: " + ex.Message);
            }
        }
        if (generation != _generation) return;

        if (Messengers.All.Any(app => _shown(app.Key)))
        {
            Dictionary<string, string>? badges = null;
            Dictionary<string, string>? revisions = null;
            try { (badges, revisions) = await Task.Run(() => NotificationBadges.ReadSnapshot()); }
            catch (Exception ex) { Diag.Log("Badges unreadable: " + ex.Message); }
            if (generation != _generation) return;
            if (badges != null) ReadBadges(badges, arrived, revisions);
        }
        _primed = true;
        Publish(arrived);
    }

    async Task<ChatMessage?> ReadToastsAsync(UserNotificationListener listener)
    {
        var all = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        if (listener != _listener) return null; // switched off meanwhile
        if (!_primed && Diag.Enabled)
        {
            // Which apps have toasts in the notification center (names and counts only, never the text).
            var apps = all.GroupBy(n => { try { return n.AppInfo?.AppUserModelId ?? "?"; } catch { return "?"; } })
                          .Select(g => $"{g.Key} ×{g.Count()}");
            Diag.Log($"Notification center: {all.Count} toasts: {string.Join(", ", apps)}");
        }

        var unread = new List<ChatMessage>();
        var present = new HashSet<uint>();
        ChatMessage? arrived = null;
        foreach (var notification in all)
        {
            uint id;
            try { id = notification.Id; } catch { continue; }
            present.Add(id);
            var latest = Read(notification, out string? appId);
            if (!_seen.TryGetValue(id, out var message) || latest?.App != message?.App
                || latest?.Time != message?.Time || latest?.Title != message?.Title || latest?.Text != message?.Text)
            {
                _seen[id] = message = latest;
                _read.Remove(id); // Windows can replace an existing toast using the same id.
                if (message != null)
                {
                    if (appId != null) _appIds[message.App] = appId;
                    // Whatever is already there when we start is old news: count it, but don't pop out.
                    if (_primed && _shown(message.App) && (arrived == null || message.Time >= arrived.Time)) arrived = message;
                    if (_logoTried.Add(message.App)) await LoadLogoAsync(notification, message.App);
                }
            }
            if (message != null && !_read.Contains(id)) unread.Add(message);
        }
        if (listener != _listener) return null; // switched off while loading a logo
        foreach (var gone in _seen.Keys.Where(id => !present.Contains(id)).ToList()) _seen.Remove(gone);
        _read.IntersectWith(present);
        unread.RemoveAll(m => _read.Contains(m.Id)); // marked read while we were busy
        unread.Sort((a, b) => a.Time.CompareTo(b.Time));
        _toasts = unread;
        return arrived;
    }

    void ReadBadges(IReadOnlyDictionary<string, string> badges, ChatMessage? arrived, IReadOnlyDictionary<string, string>? revisions = null)
    {
        foreach (var app in Messengers.All.Where(app => _shown(app.Key)))
        {
            int? count = NotificationBadges.CountFor(badges, app);
            if (count == null)
            {
                // Windows may remove a badge row when an app clears its count. A glyph is different:
                // it carries no numeric information, so keep waiting for a number in that case.
                bool hasBadge = badges.Keys.Any(id => Messengers.ForApp(id)?.Key == app.Key);
                if (hasBadge || !_counts.ContainsKey(app.Key)) continue;
                count = 0;
            }
            if (!_appIds.ContainsKey(app.Key))
            {
                string? id = badges.Keys.FirstOrDefault(key => Messengers.ForApp(key)?.Key == app.Key);
                if (id != null)
                {
                    _appIds[app.Key] = id;
                    if (_logoTried.Add(app.Key)) LoadShellLogo(app.Key, id);
                }
            }
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
            Diag.Log($"{app.Name} badge: {count}" + (refreshed ? " (new Windows badge update)" : ""));
            if (before > 0 && count == 0)
            {
                foreach (var message in _toasts.Where(m => m.App == app.Key)) _read.Add(message.Id);
                _toasts.RemoveAll(m => _read.Contains(m.Id));
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

    /// <summary>The message if the notification is from a messenger, otherwise null.</summary>
    internal static ChatMessage? Read(UserNotification notification, out string? appId)
    {
        appId = null;
        try
        {
            // AppUserModelId is the reliable one (AppInfo.Id is often empty).
            appId = notification.AppInfo?.AppUserModelId;
            string? name = string.IsNullOrEmpty(appId) ? notification.AppInfo?.DisplayInfo?.DisplayName : null;
            var texts = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
            var lines = texts?.Select(t => t.Text).ToList() ?? new List<string>();
            var message = Messengers.Parse(appId, name, lines, notification.Id, notification.CreationTime);
            if (message == null || message.Site != null) appId = null; // a browser's id doesn't open the app
            return message;
        }
        catch (Exception ex)
        {
            Diag.Log("Unreadable notification: " + ex.Message);
            return null;
        }
    }

    async Task LoadLogoAsync(UserNotification notification, string app)
    {
        try
        {
            var logo = notification.AppInfo?.DisplayInfo?.GetLogo(new Windows.Foundation.Size(64, 64));
            if (logo == null) return;
            byte[] bytes;
            using (var stream = await logo.OpenReadAsync())
            using (var input = stream.AsStreamForRead())
            using (var copy = new MemoryStream())
            {
                await input.CopyToAsync(copy);
                bytes = copy.ToArray();
            }
            if (bytes.Length == 0) return;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 64;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            _logos[app] = image;
        }
        catch (Exception ex) { Diag.Log($"{app} logo unavailable: " + ex.Message); }
    }

    /// <summary>The icon the Start menu shows for the app (when we only know its badge).</summary>
    void LoadShellLogo(string app, string appId)
    {
        var ui = _poll.Dispatcher;
        Task.Run(() =>
        {
            try { return Shell.IconOf(Shell.AppTarget(appId), 64); }
            catch { return null; }
        }).ContinueWith(t =>
        {
            if (t.Result is { } icon) ui.InvokeAsync(() => { if (_logos.TryAdd(app, icon) && _messages.Count > 0) Changed?.Invoke(null); });
        }, TaskScheduler.Default);
    }
}
