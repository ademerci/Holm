using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace WinNotch;

internal sealed class TeamsMessage
{
    public uint Id;
    /// <summary>First line of the notification: who wrote, or the chat's name.</summary>
    public string Title = "";
    public string Text = "";
    public DateTimeOffset Time;
}

/// <summary>
/// Teams messages, read from the Windows notification center: a Teams notification counts as unread until
/// you look at Teams or click it in the notch. Needs "notification access" (Settings › Privacy › Notifications).
/// Nothing is ever removed from the notification center.
/// </summary>
/// <remarks>
/// Windows only sends change events to packaged apps, so for us it usually refuses them and this looks
/// every two seconds instead: a short local call that returns ids, and only new notifications get read.
/// </remarks>
internal sealed class TeamsService
{
    public enum AccessState { Off, Allowed, Denied, Unavailable }

    static readonly Regex Whitespace = new(@"\s+");

    readonly DispatcherTimer _poll;
    readonly Dictionary<uint, TeamsMessage?> _seen = new(); // null: a notification from another app
    readonly HashSet<uint> _read = new();
    UserNotificationListener? _listener;
    bool _evented, _primed, _busy, _again, _starting, _logoTried;
    int _generation; // bumped by Stop, so a start still waiting for Windows' answer gives up
    List<TeamsMessage> _messages = new();

    public AccessState Access { get; private set; } = AccessState.Off;
    /// <summary>Unread messages, oldest first.</summary>
    public IReadOnlyList<TeamsMessage> Messages => _messages;
    /// <summary>Teams' app id as the notifications report it (new Teams, classic or the free one).</summary>
    public string? AppId { get; private set; }
    public ImageSource? Logo { get; private set; }

    /// <summary>The list changed. The argument is the newest message that just arrived, or null if none did.</summary>
    public event Action<TeamsMessage?>? Changed;

    public TeamsService(Dispatcher ui)
    {
        _poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += (_, _) => _ = RefreshAsync();
    }

    public bool IsRunning => _listener != null;

    /// <summary>
    /// Starts watching if we have notification access. <paramref name="ask"/>: request it, which may show
    /// Windows' consent prompt; otherwise only check, so hovering the notch never pops up a dialog.
    /// </summary>
    public async Task StartAsync(bool ask)
    {
        if (_listener != null || _starting) return;
        _starting = true;
        int generation = _generation;
        try
        {
            var listener = UserNotificationListener.Current;
            var access = ask ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (generation != _generation) return;
            Access = access == UserNotificationListenerAccessStatus.Allowed ? AccessState.Allowed : AccessState.Denied;
            Diag.Log("Notification access: " + access);
            if (Access != AccessState.Allowed) return;
            _listener = listener;
        }
        catch (Exception ex)
        {
            Access = AccessState.Unavailable;
            Diag.Log("Notification listener unavailable: " + ex.Message);
            return;
        }
        finally { _starting = false; }

        try
        {
            _listener.NotificationChanged += OnNotificationChanged;
            _evented = true;
        }
        catch (Exception ex) { Diag.Log("Notification events unavailable, polling instead: " + ex.Message); }
        // With events, the timer is only a safety net in case one gets lost.
        _poll.Interval = TimeSpan.FromSeconds(_evented ? 30 : 2);
        _poll.Start();
        await RefreshAsync();
    }

    public void Stop()
    {
        _generation++;
        _poll.Stop();
        if (_listener != null && _evented)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
        _listener = null;
        _evented = _primed = false;
        Access = AccessState.Off;
        _seen.Clear();
        _read.Clear();
        if (_messages.Count > 0)
        {
            _messages = new();
            Changed?.Invoke(null);
        }
    }

    void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        => _poll.Dispatcher.InvokeAsync(() => _ = RefreshAsync());

    /// <summary>You've seen them (Teams is in front, or you clicked them): clears the badge.</summary>
    public void MarkRead()
    {
        if (_messages.Count == 0) return;
        foreach (var message in _messages) _read.Add(message.Id);
        _messages = new();
        Changed?.Invoke(null);
    }

    async Task RefreshAsync()
    {
        if (_listener == null) return;
        if (_busy) { _again = true; return; }
        _busy = true;
        try
        {
            do
            {
                _again = false;
                await RefreshCoreAsync();
            } while (_again && _listener != null);
        }
        catch (Exception ex) { Diag.Log("Reading notifications failed: " + ex.Message); }
        finally { _busy = false; }
    }

    async Task RefreshCoreAsync()
    {
        var listener = _listener!;
        var all = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        if (listener != _listener) return; // switched off meanwhile

        var current = new List<TeamsMessage>();
        var present = new HashSet<uint>();
        TeamsMessage? arrived = null;
        foreach (var notification in all)
        {
            uint id;
            try { id = notification.Id; } catch { continue; }
            present.Add(id);
            if (!_seen.TryGetValue(id, out var message))
            {
                _seen[id] = message = Read(notification);
                // Whatever is already there when we start is old news: count it, but don't pop out.
                if (message != null && _primed && (arrived == null || message.Time >= arrived.Time)) arrived = message;
                if (message != null && !_logoTried)
                {
                    _logoTried = true;
                    await LoadLogoAsync(notification);
                }
            }
            if (message != null && !_read.Contains(id)) current.Add(message);
        }
        if (listener != _listener) return; // switched off while loading the logo
        foreach (var gone in _seen.Keys.Where(id => !present.Contains(id)).ToList()) _seen.Remove(gone);
        _read.IntersectWith(present);
        current.RemoveAll(m => _read.Contains(m.Id)); // marked read while we were busy
        _primed = true;

        current.Sort((a, b) => a.Time.CompareTo(b.Time));
        bool changed = arrived != null || current.Count != _messages.Count || !current.Select(m => m.Id).SequenceEqual(_messages.Select(m => m.Id));
        _messages = current;
        if (changed) Changed?.Invoke(arrived);
    }

    /// <summary>The message if the notification is from Teams, otherwise null.</summary>
    TeamsMessage? Read(UserNotification notification)
    {
        try
        {
            // AppUserModelId is the reliable one (AppInfo.Id is often empty). Matches new Teams
            // (MSTeams_…), the free consumer app (MicrosoftTeams_…) and classic Teams (com.squirrel.Teams.Teams).
            string appId = notification.AppInfo?.AppUserModelId ?? "";
            bool teams = appId.Contains("Teams", StringComparison.OrdinalIgnoreCase);
            if (!teams && appId.Length == 0)
                teams = notification.AppInfo?.DisplayInfo?.DisplayName?.Contains("Teams", StringComparison.OrdinalIgnoreCase) == true;
            if (!teams) return null;
            if (appId.Length > 0) AppId = appId;

            var texts = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
            string Line(int i) => texts != null && texts.Count > i ? Whitespace.Replace(texts[i].Text ?? "", " ").Trim() : "";
            return new TeamsMessage
            {
                Id = notification.Id,
                Title = Line(0).Length > 0 ? Line(0) : "Microsoft Teams",
                Text = Line(1),
                Time = notification.CreationTime,
            };
        }
        catch (Exception ex)
        {
            Diag.Log("Unreadable notification: " + ex.Message);
            return null;
        }
    }

    async Task LoadLogoAsync(UserNotification notification)
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
            Logo = image;
        }
        catch (Exception ex) { Diag.Log("Teams logo unavailable: " + ex.Message); }
    }
}
