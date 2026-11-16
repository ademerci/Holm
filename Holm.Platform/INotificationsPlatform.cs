using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Holm.Platform;

public enum NotificationAccess { Off, Allowed, Denied, Unavailable }

/// <summary>A notification another app showed: its text lines in order (title first).</summary>
public sealed record SystemNotification(uint Id, string? AppId, string? AppName, IReadOnlyList<string> Lines, DateTimeOffset Time);

/// <summary>
/// One look at the system's notifications: the ones it still has (null when they didn't change since the last
/// look, or can't be read) and the numbers on app icons (app id → its badge text), with a marker per badge that
/// changes whenever the app sets it again (Windows), so "one more" shows even when the number stays at 1.
/// </summary>
public sealed record NotificationSnapshot(IReadOnlyList<SystemNotification>? Toasts, IReadOnlyDictionary<string, string>? Badges,
                                          IReadOnlyDictionary<string, string>? Revisions);

/// <summary>
/// Other apps' notifications and icon badges, for the messages in the notch. Nothing is changed or removed.
/// Windows: the notification center (with the user's consent) and the taskbar badges. Linux: the notifications
/// sent on the session bus and launcher badges.
/// </summary>
public interface INotificationsPlatform
{
    /// <summary>Something may have changed: read again. Raised on any thread.</summary>
    event Action? Changed;

    NotificationAccess Access { get; }

    /// <summary>Starts watching; <paramref name="ask"/> may show the system's consent prompt.</summary>
    Task StartAsync(bool ask);
    void Stop();

    Task<NotificationSnapshot> ReadAsync();

    /// <summary>The app's icon as a PNG, once a notification or badge brought it along.</summary>
    byte[]? LogoOf(string appId);

    /// <summary>What's missing for the notifications' text, for the Messages menu; null when nothing is.</summary>
    string? AccessHint { get; }

    /// <summary>Opens the system's setting that grants access (the hint's action).</summary>
    void OpenAccessSettings();
}

public sealed class NoNotificationsPlatform : INotificationsPlatform
{
    public event Action? Changed { add { } remove { } }
    public NotificationAccess Access => NotificationAccess.Unavailable;
    public Task StartAsync(bool ask) => Task.CompletedTask;
    public void Stop() { }
    public Task<NotificationSnapshot> ReadAsync() => Task.FromResult(new NotificationSnapshot(null, null, null));
    public byte[]? LogoOf(string appId) => null;
    public string? AccessHint => null;
    public void OpenAccessSettings() { }
}
