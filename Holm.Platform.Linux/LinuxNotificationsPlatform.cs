using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Holm.Platform;
using Tmds.DBus.Protocol;
using WinNotch.Notify;

namespace Holm.Platform.Linux;

/// <summary>
/// Notifications as they are sent to the desktop's notification server on the session bus (org.freedesktop.
/// Notifications), observed over binary D-Bus via <c>org.freedesktop.DBus.Monitoring.BecomeMonitor</c> (which works
/// with both dbus-daemon and dbus-broker without text-stream parsing or consent prompts); and the numbers apps put on
/// their launcher icons (com.canonical.Unity.LauncherEntry, used by Telegram, Signal and others). Nothing is changed
/// or answered.
/// </summary>
public sealed class LinuxNotificationsPlatform : INotificationsPlatform
{
    const int Keep = 60; // notifications remembered (the newest)
    const int MaxLogoBytes = 2 * 1024 * 1024;
    const int MaxDesktopFileBytes = 256 * 1024;
    const int MaxLogos = 64;

    static readonly Regex Value = new(@"^(\s+)(string|int16|uint16|int32|uint32|int64|uint64|double|byte|boolean|variant|array \[|dict entry\(|object path|signature|struct \{|\]|\)|\}|file descriptor|unix fd)",
                                      RegexOptions.Compiled);
    static readonly Regex Markup = new(@"<[^>]{1,200}>", RegexOptions.Compiled);
    static readonly char[] InvalidEntryChars = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', '\0' }).Distinct().ToArray();

    readonly object _gate = new();
    readonly List<SystemNotification> _toasts = new();
    readonly Dictionary<string, string> _badges = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _logos = new(StringComparer.OrdinalIgnoreCase);
    DBusConnection? _monitor;
    IDisposable? _badgeWatch;
    uint _nextId = 1;
    volatile bool _running;

    public event Action? Changed;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Off;

    public string? AccessHint => Access == NotificationAccess.Unavailable ? "Session D-Bus monitoring is unavailable – app badge counts only" : null;

    public void OpenAccessSettings() { }

    public async Task StartAsync(bool ask)
    {
        if (_running) return;
        _running = true;
        if (DBusAddress.Session is not { Length: > 0 } address)
        {
            Access = NotificationAccess.Unavailable;
            WinNotch.Diag.Log("Notifications: session bus address unavailable");
        }
        else
        {
            var monitor = new DBusConnection(address);
            try
            {
                await monitor.ConnectAsync().ConfigureAwait(false);
                await monitor.BecomeMonitorAsync(OnMonitorMessage, new[]
                {
                    new MatchRule
                    {
                        Type = MessageType.MethodCall,
                        Interface = "org.freedesktop.Notifications",
                        Member = "Notify",
                    },
                }).ConfigureAwait(false);
                if (!_running)
                {
                    monitor.Dispose();
                    return;
                }
                _monitor = monitor;
                Access = NotificationAccess.Allowed;
            }
            catch (Exception ex)
            {
                monitor.Dispose();
                Access = NotificationAccess.Unavailable;
                WinNotch.Diag.Log("Notifications: D-Bus monitoring unavailable: " + ex.Message);
            }
        }
        var watch = await WatchBadgesAsync().ConfigureAwait(false);
        if (!_running)
        {
            watch?.Dispose();
            return;
        }
        _badgeWatch = watch;
        Changed?.Invoke();
    }

    public void Stop()
    {
        _running = false;
        _monitor?.Dispose();
        _monitor = null;
        _badgeWatch?.Dispose();
        _badgeWatch = null;
        lock (_gate) { _toasts.Clear(); _badges.Clear(); _logos.Clear(); }
        Access = NotificationAccess.Off;
    }

    public Task<NotificationSnapshot> ReadAsync()
    {
        lock (_gate)
            return Task.FromResult(new NotificationSnapshot(_toasts.ToList(), new Dictionary<string, string>(_badges), null));
    }

    public byte[]? LogoOf(string appId)
    {
        lock (_gate) return _logos.TryGetValue(appId, out var logo) ? logo : null;
    }

    // ───────────── notifications ─────────────

    void OnMonitorMessage(Exception? ex, DisposableMessage disposable)
    {
        if (ex != null) return;
        using (disposable)
        {
            if (!_running) return;
            Message message = disposable.Message;
            if (message.MessageType != MessageType.MethodCall
                || !string.Equals(message.InterfaceAsString, "org.freedesktop.Notifications", StringComparison.Ordinal)
                || !string.Equals(message.MemberAsString, "Notify", StringComparison.Ordinal))
                return;
            if (ReadNotify(message) is { } n) Take(n);
        }
    }

    /// <summary>Reads a binary D-Bus <c>org.freedesktop.Notifications.Notify</c> (<c>susssasa{sv}i</c>) message.</summary>
    internal static (string App, string Icon, string Summary, string Body, string? DesktopEntry)? ReadNotify(Message message)
    {
        if (message.SignatureAsString != "susssasa{sv}i") return null;
        try
        {
            var reader = message.GetBodyReader();
            string app = reader.ReadString();
            _ = reader.ReadUInt32(); // replaces_id
            string icon = reader.ReadString();
            string summary = reader.ReadString();
            string body = reader.ReadString();
            _ = reader.ReadArrayOfString(); // actions
            string? desktopEntry = null;
            ArrayEnd hintsEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(hintsEnd))
            {
                string key = reader.ReadString();
                var value = reader.ReadVariantValue();
                if (key == "desktop-entry" && value.Type == VariantValueType.String)
                    desktopEntry = value.GetString();
            }
            _ = reader.ReadInt32(); // expire_timeout
            return (app, icon, summary, body, desktopEntry);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Legacy text-parser helper kept for existing unit tests; production uses binary <see cref="ReadNotify"/>.</summary>
    internal static bool IsLastArgument(IReadOnlyList<string> block)
    {
        string last = block[^1];
        if (!last.StartsWith("   int32 ", StringComparison.Ordinal)) return false;
        int outer = 0;
        foreach (string line in block)
            if (line.Length > 3 && line.StartsWith("   ", StringComparison.Ordinal) && line[3] != ' ') outer++;
        return outer >= 7;
    }

    /// <summary>Legacy text-parser helper kept for existing unit tests; production uses binary <see cref="ReadNotify"/>.</summary>
    internal static (string App, string Icon, string Summary, string Body, string? DesktopEntry)? ParseNotify(IReadOnlyList<string> lines)
    {
        var values = new List<(int Indent, string Text)>();
        foreach (string line in lines)
        {
            var match = Value.Match(line);
            if (match.Success) values.Add((match.Groups[1].Length, line.TrimStart()));
            else if (values.Count > 0) values[^1] = (values[^1].Indent, values[^1].Text + "\n" + line); // a string over several lines
        }
        if (values.Count == 0) return null;
        int outer = values.Min(v => v.Indent);
        var top = values.Where(v => v.Indent == outer).Select(v => v.Text).ToList();
        if (top.Count < 5) return null;
        string? entry = null;
        for (int i = 0; i + 1 < values.Count; i++)
            if (values[i].Indent > outer && Str(values[i].Text) == "desktop-entry")
            {
                string variant = values[i + 1].Text;
                if (variant.StartsWith("variant", StringComparison.Ordinal)) entry = Str(variant["variant".Length..].TrimStart());
                break;
            }
        return (Str(top[0]) ?? "", Str(top[2]) ?? "", Str(top[3]) ?? "", Str(top[4]) ?? "", entry);
    }

    static string? Str(string text)
        => text.StartsWith("string \"", StringComparison.Ordinal) && text.EndsWith('"') && text.Length >= 9 ? text[8..^1] : null;

    void Take((string App, string Icon, string Summary, string Body, string? DesktopEntry) n)
    {
        string appId = n.DesktopEntry is { Length: > 0 } entry ? entry : n.App;
        if (appId.Length == 0) return;
        // A browser names the site in the body (Chrome: the first line, as a link): put it last, where Windows has it.
        var lines = new List<string> { Plain(n.Summary) };
        lines.AddRange(Plain(n.Body).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        int site = lines.FindIndex(1, l => l.Length <= 60 && Messengers.All.Any(m => m.WebHosts.Any(h => l.Contains(h, StringComparison.OrdinalIgnoreCase))));
        if (site > 0 && site < lines.Count - 1) { string host = lines[site]; lines.RemoveAt(site); lines.Add(host); }

        bool needLogo;
        lock (_gate)
        {
            // An app updating its notification sends the same again: one entry.
            _toasts.RemoveAll(t => t.AppId == appId && t.Lines.SequenceEqual(lines));
            _toasts.Add(new SystemNotification(_nextId++, appId, n.App, lines, DateTimeOffset.Now));
            if (_toasts.Count > Keep) _toasts.RemoveRange(0, _toasts.Count - Keep);
            needLogo = !_logos.ContainsKey(appId) && _logos.Count < MaxLogos;
        }

        if (needLogo && IconFile(n.Icon, n.DesktopEntry ?? n.App) is { } file
            && LinuxFileChecks.ReadBoundedFile(file, MaxLogoBytes) is { } logoBytes)
        {
            lock (_gate)
            {
                if (_running && !_logos.ContainsKey(appId) && _logos.Count < MaxLogos)
                    _logos[appId] = logoBytes;
            }
        }
        Changed?.Invoke();
    }

    static string Plain(string text) => System.Net.WebUtility.HtmlDecode(Markup.Replace(text, ""));

    internal static bool IsSafeDesktopEntry(string? desktopEntry)
    {
        if (string.IsNullOrWhiteSpace(desktopEntry) || desktopEntry.Length > 128) return false;
        if (desktopEntry is "." or ".." || desktopEntry.Contains("..", StringComparison.Ordinal)) return false;
        return desktopEntry.IndexOfAny(InvalidEntryChars) < 0;
    }

    /// <summary>The app's icon: the notification's own PNG, or the icon its desktop file names.</summary>
    internal static string? IconFile(string icon, string desktopEntry)
    {
        if (icon.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(icon, UriKind.Absolute, out var uri) || !uri.IsFile) icon = "";
            else icon = uri.LocalPath;
        }
        if (icon.StartsWith('/') && icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && LinuxFileChecks.IsRegularFile(icon, MaxLogoBytes))
            return icon;
        if (LinuxAppVolumePlatform.FindIcon(icon) is { } named && LinuxFileChecks.IsRegularFile(named, MaxLogoBytes))
            return named;
        if (!IsSafeDesktopEntry(desktopEntry)) return null;
        foreach (string folder in new[] { "/usr/share/applications", "/var/lib/flatpak/exports/share/applications",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications") })
        {
            string file = Path.Combine(folder, desktopEntry + ".desktop");
            if (!LinuxFileChecks.IsRegularFile(file, MaxDesktopFileBytes)) continue;
            try
            {
                string? name = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("Icon=", StringComparison.Ordinal))?[5..].Trim();
                if (name != null && LinuxAppVolumePlatform.FindIcon(name) is { } found && LinuxFileChecks.IsRegularFile(found, MaxLogoBytes))
                    return found;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { }
        }
        return null;
    }

    // ───────────── launcher badges ─────────────

    async Task<IDisposable?> WatchBadgesAsync()
    {
        try
        {
            if (await Bus.Session.ConnectAsync().ConfigureAwait(false) is not { } connection) return null;
            var rule = new MatchRule { Type = MessageType.Signal, Interface = "com.canonical.Unity.LauncherEntry", Member = "Update" };
            return await connection.AddMatchAsync(rule, static (Message m, object? _) => ReadBadge(m),
                (Notification<(string App, int Count, bool Visible)?> update) =>
                {
                    if (!update.HasValue || update.Value is not { } badge) return;
                    lock (_gate) _badges[badge.App] = (badge.Visible ? badge.Count : 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Changed?.Invoke();
                }, false, ObserverFlags.None).ConfigureAwait(false);
        }
        catch { return null; }
    }

    /// <summary>Update(s app_uri, a{sv} properties): "application://org.telegram.desktop.desktop", count and count-visible.</summary>
    static (string App, int Count, bool Visible)? ReadBadge(Message message)
    {
        if (message.SignatureAsString != "sa{sv}") return null;
        var reader = message.GetBodyReader();
        string uri = reader.ReadString();
        long? count = null;
        bool? visible = null;
        ArrayEnd end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            string key = reader.ReadString();
            var value = reader.ReadVariantValue();
            if (key == "count" && value.Type is VariantValueType.Int64) count = value.GetInt64();
            else if (key == "count-visible" && value.Type is VariantValueType.Bool) visible = value.GetBool();
        }
        string app = uri.StartsWith("application://", StringComparison.Ordinal) ? uri["application://".Length..] : uri;
        if (app.EndsWith(".desktop", StringComparison.Ordinal)) app = app[..^".desktop".Length];
        if (app.Length == 0 || (count == null && visible == null)) return null;
        return (app, (int)Math.Clamp(count ?? 0, 0, 9999), visible ?? count > 0);
    }
}
