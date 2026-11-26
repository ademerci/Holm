using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using WinNotch;
using WinNotch.Notify;

namespace Holm.Platform.Windows;

/// <summary>
/// The WPF app's MessagesService's reading part: the notification center's toasts (with "notification access")
/// and the taskbar badges from Windows' notification database (NotificationBadges, linked), which needs no access.
/// Windows only sends change events to packaged apps, so the database's folder is watched and its marker checked
/// every 2 s; the toasts are only asked for when their fingerprint in the database moved.
/// </summary>
public sealed class WindowsNotificationsPlatform : INotificationsPlatform
{
    UserNotificationListener? _listener;
    FileSystemWatcher? _watcher;
    Timer? _poll, _settle;
    string? _stamp, _toastPrint;
    bool _running;
    int _generation;
    readonly ConcurrentDictionary<string, byte[]> _logos = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _logoTried = new(StringComparer.OrdinalIgnoreCase);

    public event Action? Changed;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Off;
    public bool RetainsToastsAcrossRestarts => true;

    public string? AccessHint => Access switch
    {
        NotificationAccess.Denied => "Allow notification access…",
        NotificationAccess.Unavailable => "Windows doesn't share notification text here – app badge counts only",
        _ => null,
    };

    public void OpenAccessSettings()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true })?.Dispose(); } catch { }
    }

    public async Task StartAsync(bool ask)
    {
        int generation = _generation;
        if (!_running)
        {
            _running = true;
            try
            {
                string folder = NotificationBadges.DefaultFolder;
                if (Directory.Exists(folder))
                {
                    _watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    FileSystemEventHandler changed = (_, _) => _settle?.Change(700, Timeout.Infinite);
                    _watcher.Changed += changed;
                    _watcher.Created += changed;
                    _watcher.Deleted += changed;
                    _watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception ex) { Diag.Log("Notification database can't be watched: " + ex.Message); }
            // A notification arriving means a burst of database writes: look once, shortly after the first.
            _settle = new Timer(_ => CheckStamp(), null, Timeout.Infinite, Timeout.Infinite);
            // Windows keeps the database open, so the watcher often only hears of a write once someone else opens
            // the file. Checking the marker costs a fifth of a millisecond, so check it often.
            _poll = new Timer(_ => CheckStamp(), null, 2000, 2000);
        }
        if (_listener != null) return;
        try
        {
            var listener = UserNotificationListener.Current;
            var access = ask ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (generation != _generation) return;
            Access = access == UserNotificationListenerAccessStatus.Allowed ? NotificationAccess.Allowed : NotificationAccess.Denied;
            Diag.Log("Notification access: " + access);
            if (Access == NotificationAccess.Allowed)
            {
                _listener = listener;
                try { listener.NotificationChanged += OnNotificationChanged; }
                catch (Exception ex) { Diag.Log("Notification events unavailable: " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Access = NotificationAccess.Unavailable;
            Diag.Log("Notification listener unavailable: " + ex.Message);
        }
        Changed?.Invoke();
    }

    void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        _toastPrint = null; // ask the notification center again
        Changed?.Invoke();
    }

    void CheckStamp()
    {
        if (!_running) return;
        string? stamp = NotificationBadges.Stamp();
        if (stamp != null && stamp == _stamp) return;
        Changed?.Invoke();
    }

    public void Stop()
    {
        _generation++;
        _running = false;
        _poll?.Dispose();
        _settle?.Dispose();
        _poll = _settle = null;
        _watcher?.Dispose();
        _watcher = null;
        if (_listener != null)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { }
        }
        _listener = null;
        Access = NotificationAccess.Off;
        _stamp = _toastPrint = null;
    }

    public async Task<NotificationSnapshot> ReadAsync()
    {
        // Taken before reading, so whatever Windows writes meanwhile makes the timer look again.
        _stamp = NotificationBadges.Stamp();
        Dictionary<string, string>? badges = null, revisions = null;
        string? toastPrint = null;
        try { (badges, revisions, toastPrint) = await Task.Run(() => NotificationBadges.ReadSnapshot()); }
        catch (Exception ex) { Diag.Log("Badges unreadable: " + ex.Message); }

        List<SystemNotification>? toasts = null;
        // Only a badge or another app's notification changed: the toasts we have are still right.
        if (_listener is { } listener && (toastPrint == null || toastPrint != _toastPrint))
        {
            try
            {
                // Access can be revoked while running. Windows may return an empty list instead of throwing.
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    Access = NotificationAccess.Allowed;
                    toasts = new();
                    foreach (var notification in await listener.GetNotificationsAsync(NotificationKinds.Toast))
                    {
                        if (Convert(notification) is not { } toast) continue;
                        toasts.Add(toast);
                        if (toast.AppId is { } id && _logoTried.Add(id)) await LoadLogoAsync(notification, id);
                    }
                    _toastPrint = toastPrint;
                }
                else
                {
                    Access = NotificationAccess.Denied;
                    toasts = new();
                    _toastPrint = null;
                }
            }
            catch (Exception ex) { Diag.Log("Reading notification toasts failed: " + ex.Message); }
        }
        return new NotificationSnapshot(toasts, badges, revisions);
    }

    static SystemNotification? Convert(UserNotification notification)
    {
        try
        {
            // AppUserModelId is the reliable one (AppInfo.Id is often empty).
            string? appId = notification.AppInfo?.AppUserModelId;
            string? name = notification.AppInfo?.DisplayInfo?.DisplayName;
            var lines = notification.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements()
                ?.Select(t => t.Text ?? "").ToList() ?? new List<string>();
            return new SystemNotification(notification.Id, string.IsNullOrEmpty(appId) ? null : appId, name, lines, notification.CreationTime);
        }
        catch (Exception ex)
        {
            Diag.Log("Unreadable notification: " + ex.Message);
            return null;
        }
    }

    const int MaxLogoBytes = 8 * 1024 * 1024;

    async Task LoadLogoAsync(UserNotification notification, string appId)
    {
        try
        {
            var logo = notification.AppInfo?.DisplayInfo?.GetLogo(new global::Windows.Foundation.Size(64, 64));
            if (logo == null) return;
            using var stream = await logo.OpenReadAsync();
            if (stream.Size > (ulong)MaxLogoBytes) return;
            using var input = stream.AsStreamForRead();
            using var copy = new MemoryStream();
            var buffer = new byte[16 * 1024];
            int total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxLogoBytes) return;
                copy.Write(buffer, 0, read);
            }
            if (total > 0) _logos[appId] = copy.ToArray();
        }
        catch (Exception ex) { Diag.Log("Logo unavailable: " + ex.Message); }
    }

    public byte[]? LogoOf(string appId) => _logos.TryGetValue(appId, out var logo) ? logo : null;
}
