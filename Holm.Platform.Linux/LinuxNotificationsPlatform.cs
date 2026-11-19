using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Holm.Platform;
using Tmds.DBus.Protocol;
using WinNotch.Notify;

namespace Holm.Platform.Linux;

/// <summary>
/// Notifications as they are sent to the desktop's notification server on the session bus (org.freedesktop.
/// Notifications), seen through dbus-monitor, which works with both dbus-daemon and dbus-broker and needs no
/// consent; and the numbers apps put on their launcher icons (com.canonical.Unity.LauncherEntry, used by
/// Telegram, Signal and others). Nothing is changed or answered.
/// </summary>
public sealed class LinuxNotificationsPlatform : INotificationsPlatform
{
    const int Keep = 60; // notifications remembered (the newest)

    static readonly Regex Header = new(@"^(method call|method return|signal|error) time=", RegexOptions.Compiled);
    static readonly Regex Value = new(@"^(\s+)(string|int16|uint16|int32|uint32|int64|uint64|double|byte|boolean|variant|array \[|dict entry\(|object path|signature|struct \{|\]|\)|\}|file descriptor|unix fd)",
                                      RegexOptions.Compiled);
    static readonly Regex Markup = new(@"<[^>]{1,200}>", RegexOptions.Compiled);

    readonly object _gate = new();
    readonly List<SystemNotification> _toasts = new();
    readonly Dictionary<string, string> _badges = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, byte[]> _logos = new(StringComparer.OrdinalIgnoreCase);
    Process? _monitor;
    IDisposable? _badgeWatch;
    uint _nextId = 1;
    bool _running;

    public event Action? Changed;

    public NotificationAccess Access { get; private set; } = NotificationAccess.Off;

    public string? AccessHint => Access == NotificationAccess.Unavailable ? "dbus-monitor is missing – app badge counts only" : null;

    public void OpenAccessSettings() { }

    public async Task StartAsync(bool ask)
    {
        if (_running) return;
        _running = true;
        try
        {
            var start = new ProcessStartInfo("dbus-monitor")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("--session");
            start.ArgumentList.Add("type='method_call',interface='org.freedesktop.Notifications',member='Notify'");
            _monitor = Process.Start(start);
            if (_monitor == null) Access = NotificationAccess.Unavailable;
            else
            {
                Access = NotificationAccess.Allowed;
                var monitor = _monitor;
                _ = Task.Run(() => ReadMonitor(monitor));
                _ = Task.Run(async () => { try { await monitor.StandardError.ReadToEndAsync(); } catch { } });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Access = NotificationAccess.Unavailable;
            WinNotch.Diag.Log("Notifications: dbus-monitor unavailable");
        }
        _badgeWatch = await WatchBadgesAsync();
        Changed?.Invoke();
    }

    public void Stop()
    {
        _running = false;
        try { if (_monitor is { HasExited: false }) _monitor.Kill(); } catch { }
        _monitor?.Dispose();
        _monitor = null;
        _badgeWatch?.Dispose();
        _badgeWatch = null;
        lock (_gate) { _toasts.Clear(); _badges.Clear(); }
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

    void ReadMonitor(Process monitor)
    {
        var block = new List<string>();
        bool notify = false;
        try
        {
            string? line;
            while ((line = monitor.StandardOutput.ReadLine()) != null)
            {
                if (Header.IsMatch(line))
                {
                    if (notify) Take(block);
                    block.Clear();
                    notify = line.StartsWith("method call", StringComparison.Ordinal) && line.Contains("member=Notify", StringComparison.Ordinal);
                    continue;
                }
                if (!notify) continue;
                block.Add(line);
                // The timeout is the call's last argument: take it now, not when the next message comes.
                if (IsLastArgument(block))
                {
                    Take(block);
                    block.Clear();
                    notify = false;
                }
            }
            if (notify) Take(block);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Whether the block ends with Notify's timeout: an int32 at the outer level after at least six values.</summary>
    internal static bool IsLastArgument(IReadOnlyList<string> block)
    {
        string last = block[^1];
        if (!last.StartsWith("   int32 ", StringComparison.Ordinal)) return false;
        int outer = 0;
        foreach (string line in block)
            if (line.Length > 3 && line.StartsWith("   ", StringComparison.Ordinal) && line[3] != ' ') outer++;
        return outer >= 7;
    }

    /// <summary>The values of a Notify call: app name, replaced id, icon, summary, body, actions, hints, timeout.</summary>
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

    void Take(List<string> block)
    {
        if (ParseNotify(block) is not { } n) return;
        string appId = n.DesktopEntry is { Length: > 0 } entry ? entry : n.App;
        if (appId.Length == 0) return;
        // A browser names the site in the body (Chrome: the first line, as a link): put it last, where Windows has it.
        var lines = new List<string> { Plain(n.Summary) };
        lines.AddRange(Plain(n.Body).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        int site = lines.FindIndex(1, l => l.Length <= 60 && Messengers.All.Any(m => m.WebHosts.Any(h => l.Contains(h, StringComparison.OrdinalIgnoreCase))));
        if (site > 0 && site < lines.Count - 1) { string host = lines[site]; lines.RemoveAt(site); lines.Add(host); }
        lock (_gate)
        {
            // An app updating its notification sends the same again: one entry.
            _toasts.RemoveAll(t => t.AppId == appId && t.Lines.SequenceEqual(lines));
            _toasts.Add(new SystemNotification(_nextId++, appId, n.App, lines, DateTimeOffset.Now));
            if (_toasts.Count > Keep) _toasts.RemoveRange(0, _toasts.Count - Keep);
            if (!_logos.ContainsKey(appId) && IconFile(n.Icon, n.DesktopEntry ?? n.App) is { } file)
                try { _logos[appId] = File.ReadAllBytes(file); } catch (IOException) { }
        }
        Changed?.Invoke();
    }

    static string Plain(string text) => System.Net.WebUtility.HtmlDecode(Markup.Replace(text, ""));

    /// <summary>The app's icon: the notification's own PNG, or the icon its desktop file names.</summary>
    static string? IconFile(string icon, string desktopEntry)
    {
        if (icon.StartsWith("file://", StringComparison.Ordinal)) icon = Uri.UnescapeDataString(icon[7..]);
        if (icon.StartsWith('/') && icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(icon)) return icon;
        if (LinuxAppVolumePlatform.FindIcon(icon) is { } named) return named;
        foreach (string folder in new[] { "/usr/share/applications", "/var/lib/flatpak/exports/share/applications",
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/applications") })
        {
            string file = Path.Combine(folder, desktopEntry + ".desktop");
            if (!File.Exists(file)) continue;
            try
            {
                string? name = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("Icon=", StringComparison.Ordinal))?[5..].Trim();
                if (name != null && LinuxAppVolumePlatform.FindIcon(name) is { } found) return found;
            }
            catch (IOException) { }
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
