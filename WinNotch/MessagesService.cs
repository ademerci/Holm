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
/// Messages from chat and mail apps (Teams, WhatsApp, Discord, Outlook, …), read from the Windows notification
/// center: a notification counts as unread until you look at its app or click it in the notch. That needs
/// "notification access" (Settings › Privacy › Notifications). Nothing is ever removed or changed.
/// </summary>
/// <remarks>
/// Teams in its own notification style shows messages in pop-ups no other app can read, so for Teams we also
/// read the number on its taskbar icon from the notification database; that works in every style and needs
/// no access. Windows only sends change events to packaged apps, so instead we watch the database's folder,
/// which Windows writes whenever a notification or badge changes, and only then look. A slow timer is the
/// safety net.
/// </remarks>
internal sealed class MessagesService
{
    public enum AccessState { Off, Allowed, Denied, Unavailable }

    static readonly Messenger Teams = Messengers.Get("teams")!;

    readonly Func<string, bool> _shown; // is this messenger switched on?
    readonly DispatcherTimer _poll, _settle, _badgePop;
    FileSystemWatcher? _watcher;
    readonly Dictionary<uint, ChatMessage?> _seen = new(); // null: a notification from another app
    readonly HashSet<uint> _read = new();
    readonly Dictionary<string, string> _appIds = new();
    readonly Dictionary<string, ImageSource> _logos = new();
    readonly HashSet<string> _logoTried = new();
    UserNotificationListener? _listener;
    bool _running, _evented, _primed, _busy, _again, _starting, _teamsToasts;
    int _generation; // bumped by Stop, so a start still waiting for Windows' answer gives up
    List<ChatMessage> _toasts = new(); // unread notifications, oldest first
    List<ChatMessage> _messages = new();
    readonly BadgeCounter _teamsCount = new(); // the number on Teams' taskbar icon
    DateTimeOffset _teamsCountTime;

    public AccessState Access { get; private set; } = AccessState.Off;
    /// <summary>Unread messages of the messengers that are switched on, oldest first.</summary>
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public int UnreadCount => _messages.Sum(m => m.Count);
    /// <summary>Teams reports its count but has never sent a notification: it shows messages in its own style.</summary>
    public bool TeamsOnlyCounts => _teamsCount.Count != null && !_teamsToasts;

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
        // Teams' number went up: wait a moment for its notification, which says who wrote what.
        _badgePop = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromMilliseconds(1500) };
        _badgePop.Tick += (_, _) => { _badgePop.Stop(); Publish(null, popTeamsCount: true); };
    }

    public bool IsRunning => _running;

    /// <summary>The app id Windows reported for a messenger's notifications or badge, to open it.</summary>
    public string? AppIdOf(string app) => _appIds.TryGetValue(app, out var id) ? id : null;

    /// <summary>The messenger's own icon, once a notification brought it along or its badge told us where it lives.</summary>
    public ImageSource? LogoOf(string app) => _logos.TryGetValue(app, out var logo) ? logo : null;

    /// <summary>
    /// Starts watching. With notification access, every messenger's notifications; Teams' count works without.
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
                bool watching = WatchDatabase();
                _poll.Interval = TimeSpan.FromSeconds(watching ? 60 : 10);
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
        _teamsCount.Reset();
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
        if ((app == null || app == Teams.Key) && _teamsCount.Unread > 0)
        {
            _teamsCount.MarkSeen();
            any = true;
        }
        if (any) Publish(null);
    }

    /// <summary>A messenger was switched on or off in the menu.</summary>
    public void Refilter()
    {
        if (!_running) return;
        Publish(null);
        _ = RefreshAsync(); // Teams' count is only read while Teams is on
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
        if (_listener is { } listener) arrived = await ReadToastsAsync(listener);
        if (generation != _generation) return;

        if (_shown(Teams.Key))
        {
            Dictionary<string, string>? badges = null;
            try { badges = await Task.Run(() => NotificationBadges.Read()); }
            catch (Exception ex) { Diag.Log("Badges unreadable: " + ex.Message); }
            if (generation != _generation) return;
            if (badges != null) ReadTeamsBadge(badges, arrived);
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
            if (!_seen.TryGetValue(id, out var message))
            {
                _seen[id] = message = Read(notification, out string? appId);
                if (message != null)
                {
                    if (appId != null) _appIds[message.App] = appId;
                    if (message.App == Teams.Key) _teamsToasts = true;
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

    void ReadTeamsBadge(IReadOnlyDictionary<string, string> badges, ChatMessage? arrived)
    {
        int? count = NotificationBadges.CountFor(badges, Teams);
        if (count == null) return;
        if (!_appIds.ContainsKey(Teams.Key))
        {
            string? id = badges.Keys.FirstOrDefault(k => k.EndsWith("!MSTeams", StringComparison.OrdinalIgnoreCase))
                      ?? badges.Keys.FirstOrDefault(k => Messengers.ForApp(k) == Teams);
            if (id != null)
            {
                _appIds[Teams.Key] = id;
                if (_logoTried.Add(Teams.Key)) LoadShellLogo(Teams.Key, id);
            }
        }

        bool first = _teamsCount.Count == null;
        bool up = _teamsCount.Update(count.Value);
        Diag.Log($"Teams badge: {count}");
        if (first) return;
        if (count == 0)
        {
            // Everything read in Teams: its notifications are old news too.
            foreach (var message in _toasts.Where(m => m.App == Teams.Key)) _read.Add(message.Id);
            _toasts.RemoveAll(m => _read.Contains(m.Id));
            _badgePop.Stop();
        }
        if (up)
        {
            _teamsCountTime = DateTimeOffset.Now;
            // A Teams notification came along: it says who wrote what, no need for the bare number.
            if (arrived?.App == Teams.Key) _badgePop.Stop();
            else if (_shown(Teams.Key)) { _badgePop.Stop(); _badgePop.Start(); }
        }
    }

    /// <summary>Builds the list from the unread notifications and Teams' number, and tells the window.</summary>
    void Publish(ChatMessage? arrived, bool popTeamsCount = false)
    {
        var list = _toasts.Where(m => _shown(m.App)).ToList();
        ChatMessage? countOnly = null;
        if (_shown(Teams.Key))
        {
            int fromBadge = _teamsCount.Unread;
            int withText = list.Count(m => m.App == Teams.Key);
            if (fromBadge > withText)
            {
                countOnly = new ChatMessage
                {
                    App = Teams.Key, Title = "Microsoft Teams", CountOnly = true, Count = fromBadge - withText, Time = _teamsCountTime,
                    Text = fromBadge == 1 ? "1 unread message" : $"{fromBadge} unread messages",
                };
                list.Add(countOnly);
                list.Sort((a, b) => a.Time.CompareTo(b.Time));
            }
        }
        if (popTeamsCount) arrived = countOnly;

        bool changed = arrived != null || list.Count != _messages.Count
                    || !list.Zip(_messages).All(p => p.First.Id == p.Second.Id && p.First.Count == p.Second.Count && p.First.CountOnly == p.Second.CountOnly);
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
