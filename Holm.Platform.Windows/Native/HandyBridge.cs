using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinNotch;

/// <summary>
/// Works alongside Handy (github.com/cjpais/Handy, offline dictation): tells when it is dictating by watching its
/// "Recording" overlay show and hide, and can keep that overlay off screen so the notch shows the state instead.
/// </summary>
internal sealed partial class HandyBridge : IDisposable
{
    const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    const uint SWP_NOZORDER = 0x0004, SWP_ASYNCWINDOWPOS = 0x4000;
    const int OffScreen = -32000;
    internal const string OverlayClass = "Tauri Window", OverlayTitle = "Recording";
    internal const string LatestReleaseUrl = "https://api.github.com/repos/cjpais/Handy/releases/latest";
    const string DownloadPrefix = "https://github.com/cjpais/Handy/releases/download/";
    internal const long MaxInstallerBytes = 200L * 1024 * 1024;
    const int MaxMetadataBytes = 1024 * 1024;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, char[] name, int max);
    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, char[] text, int max);
    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);

    readonly WinEventProc _proc; // kept alive while the hooks are in
    /// <summary>The UI thread's loop (the WPF app: its Dispatcher, see HandyBridge.Wpf.cs).</summary>
    readonly Action<Action> _post, _invoke;
    readonly Func<bool> _onUiThread;
    /// <summary>
    /// While dictating, a look once a second: live, the first hide after start once went unheard and the notch kept
    /// saying Listening. Events stay the fast path; this only makes sure the state can't get stuck.
    /// </summary>
    readonly Action<bool> _check; // true starts it, false stops it
    IntPtr _showHook, _moveHook;
    IntPtr _overlay;
    uint _overlayPid;
    bool _dictating, _hideOverlay;

    /// <summary>Posted to the UI thread when Handy starts (true) or stops (false) recording or transcribing.</summary>
    public event Action<bool>? DictatingChanged;

    public bool IsDictating => _dictating;

    /// <summary>Keeps Handy's overlay off every screen while it shows. It is never hidden: its visibility is the signal.</summary>
    public bool HideOverlay
    {
        get => _hideOverlay;
        set { _hideOverlay = value; if (value) MoveAway(); }
    }

    /// <summary>Create on the UI thread: out-of-context events arrive on the thread that hooked them.</summary>
    /// <param name="post">Runs an action on the UI thread later.</param>
    /// <param name="invoke">Runs an action on the UI thread and waits for it.</param>
    /// <param name="onUiThread">Whether this is the UI thread.</param>
    /// <param name="ticker">Makes the once-a-second check of the state from the action to run.</param>
    public HandyBridge(Action<Action> post, Action<Action> invoke, Func<bool> onUiThread, Func<Action, Action<bool>> ticker)
    {
        _post = post;
        _invoke = invoke;
        _onUiThread = onUiThread;
        _check = ticker(Update);
        _proc = OnEvent;
        // Only show/hide/destroy system-wide; moves (which include every mouse move) are hooked for Handy alone.
        _showHook = SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_HIDE, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        for (IntPtr w = FindWindowEx(IntPtr.Zero, IntPtr.Zero, OverlayClass, OverlayTitle); w != IntPtr.Zero;
             w = FindWindowEx(IntPtr.Zero, w, OverlayClass, OverlayTitle))
            if (IsHandy(w)) { Adopt(w); break; }
        Update();
    }

    void OnEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 /* OBJID_WINDOW */ || idChild != 0 || hwnd == IntPtr.Zero) return;
        if (hwnd != _overlay)
        {
            if (evt != EVENT_OBJECT_SHOW || !IsOverlay(hwnd)) return;
            Adopt(hwnd);
        }
        if (evt == EVENT_OBJECT_DESTROY) { Forget(); Update(); return; }
        if (evt == EVENT_OBJECT_LOCATIONCHANGE) { MoveAway(); return; }
        Update();
    }

    static bool IsOverlay(IntPtr hwnd)
    {
        var buffer = new char[64];
        int n = GetClassName(hwnd, buffer, buffer.Length);
        if (!buffer.AsSpan(0, n).SequenceEqual(OverlayClass)) return false;
        n = GetWindowText(hwnd, buffer, buffer.Length);
        return buffer.AsSpan(0, n).SequenceEqual(OverlayTitle) && IsHandy(hwnd);
    }

    static bool IsHandy(IntPtr hwnd) => string.Equals(Native.ProcessNameOf(hwnd), "handy", StringComparison.OrdinalIgnoreCase);

    void Adopt(IntPtr hwnd)
    {
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return; // pid 0 would hook moves system-wide
        _overlay = hwnd;
        if (pid == _overlayPid && _moveHook != IntPtr.Zero) return;
        if (_moveHook != IntPtr.Zero) UnhookWinEvent(_moveHook);
        _overlayPid = pid;
        _moveHook = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, _proc, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    void Forget()
    {
        if (_moveHook != IntPtr.Zero) UnhookWinEvent(_moveHook);
        (_moveHook, _overlay, _overlayPid) = (IntPtr.Zero, IntPtr.Zero, 0);
    }

    // Events are queued and may arrive late, so the window's visibility now is the truth, not the event's kind.
    void Update()
    {
        bool on = _overlay != IntPtr.Zero && IsWindowVisible(_overlay);
        if (on) MoveAway();
        if (on == _dictating) return;
        _dictating = on;
        _check(on);
        // Posted, so a handler attached right after construction still hears the start-up state.
        _post(() => DictatingChanged?.Invoke(on));
    }

    void MoveAway()
    {
        // Already off every screen (including after our own move): leave it.
        if (!_hideOverlay || _overlay == IntPtr.Zero || !IsWindowVisible(_overlay) || Native.MonitorFromWindow(_overlay, 0 /* MONITOR_DEFAULTTONULL */) == IntPtr.Zero) return;
        // Async so a hung Handy can't freeze the notch.
        Native.SetWindowPos(_overlay, IntPtr.Zero, OffScreen, OffScreen, 0, 0, Native.SWP_NOSIZE | SWP_NOZORDER | Native.SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);
    }

    public void Dispose()
    {
        if (!_onUiThread()) { _invoke(Dispose); return; }
        _check(false);
        Forget();
        if (_showHook != IntPtr.Zero) UnhookWinEvent(_showHook);
        _showHook = IntPtr.Zero;
    }

    // ---- Install and settings ----

    /// <summary>Handy's own settings; overlay_style lives in its "settings" object.</summary>
    public static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.pais.handy", "settings_store.json");

    /// <summary>"none", "minimal" or "live"; null when missing or unreadable. With "none" the overlay never shows.</summary>
    public static string? OverlayStyle(string settingsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
                ? Text(settings, "overlay_style") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The installed handy.exe, or null.</summary>
    public static string? FindInstall()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Handy");
            string? path = ParseInstallLocation(key?.GetValue("InstallLocation") as string, key?.GetValue("DisplayIcon") as string);
            if (path != null && File.Exists(path)) return path;
        }
        catch { } // a locked-down registry just means: look in the usual place
        string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handy", "handy.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>handy.exe from the uninstall entry's values (either may be quoted; DisplayIcon may end with ",0").</summary>
    internal static string? ParseInstallLocation(string? installLocation, string? displayIcon)
    {
        string location = Unquote(installLocation);
        if (location.Length > 0) return Path.Combine(location, "handy.exe");
        string icon = Unquote(displayIcon);
        int comma = icon.LastIndexOf(',');
        if (comma > 0 && int.TryParse(icon[(comma + 1)..], out _)) icon = Unquote(icon[..comma]);
        return icon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? icon : null;
    }

    static string Unquote(string? value) => (value ?? "").Trim().Trim('"').Trim();

    /// <summary>The Windows x64 installer of Handy's latest release, or null.</summary>
    public static async Task<(string Url, string? Sha256)?> FindInstallerUrlAsync(HttpClient http, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(LatestReleaseUrl);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var metadata = new MemoryStream();
        await CopyBoundedAsync(stream, metadata, MaxMetadataBytes, timeout.Token).ConfigureAwait(false);
        string json = System.Text.Encoding.UTF8.GetString(metadata.GetBuffer(), 0, (int)metadata.Length);
        return PickInstaller(json) is { } url ? (url, PickDigest(json, url)) : null;
    }

    /// <summary>The download URL of the one "_x64-setup.exe" asset, only if it is an https download from Handy's own releases.</summary>
    internal static string? PickInstaller(string releaseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            return assets.EnumerateArray()
                .Where(a => a.ValueKind == JsonValueKind.Object && (Text(a, "name")?.EndsWith("_x64-setup.exe", StringComparison.OrdinalIgnoreCase) ?? false))
                .Select(a => Text(a, "browser_download_url"))
                .FirstOrDefault(IsTrustedDownload);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The SHA-256 GitHub lists for that asset ("digest": "sha256:…"), in lower-case hex; null if it lists none.</summary>
    internal static string? PickDigest(string releaseJson, string url)
    {
        try
        {
            using var document = JsonDocument.Parse(releaseJson);
            if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
            string? digest = assets.EnumerateArray().Where(a => a.ValueKind == JsonValueKind.Object && Text(a, "browser_download_url") == url)
                                   .Select(a => Text(a, "digest")).FirstOrDefault();
            if (digest == null || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            string hex = digest[7..].ToLowerInvariant();
            return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
        }
        catch (JsonException) { return null; }
    }

    static bool IsTrustedDownload(string? url) =>
        url != null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "github.com" && uri.IsDefaultPort && uri.AbsoluteUri.StartsWith(DownloadPrefix, StringComparison.Ordinal)
        && uri.AbsolutePath.EndsWith("_x64-setup.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Downloads the installer to a fresh folder under %TEMP%\Holm\handy and returns its path. Runs nothing.</summary>
    /// <param name="sha256">The digest GitHub lists for it: a download that doesn't match is deleted and refused.</param>
    public static async Task<string> DownloadInstallerAsync(HttpClient http, string url, string? sha256, CancellationToken ct)
    {
        if (!IsTrustedDownload(url)) throw new InvalidDataException("That is not a Handy release download.");
        string folder = Path.Combine(Path.GetTempPath(), "Holm", "handy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Handy-x64-setup.exe");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            using var request = Request(url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxInstallerBytes) throw new InvalidDataException("The Handy installer is too large.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, true))
                if (await CopyBoundedAsync(input, output, MaxInstallerBytes, timeout.Token).ConfigureAwait(false) == 0)
                    throw new InvalidDataException("The Handy installer download was empty.");
            if (sha256 != null)
            {
                await using var written = File.OpenRead(path);
                string actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(written, timeout.Token).ConfigureAwait(false)).ToLowerInvariant();
                if (actual != sha256) throw new InvalidDataException("The Handy installer doesn't match its release.");
            }
            return path;
        }
        catch
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
            throw;
        }
    }

    // Same request shape as UpdateService's (its helpers are private there): GitHub refuses requests without a User-Agent.
    static HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Holm/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("The download exceeds its size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return total;
    }
}
