using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>The WPF app's HandyBridge (linked): Handy's overlay window says when it listens, and is moved out of sight.</summary>
public sealed class WindowsDictationPlatform : IDictationPlatform
{
    static readonly HttpClient HandyHttp = new() { DefaultRequestHeaders = { { "User-Agent", "Holm" } } };
    HandyBridge? _bridge;

    public string? FindHandy() => HandyBridge.FindInstall();
    public bool ReportsListening => true;
    public event Action<bool>? ListeningChanged;

    public void Start(Action<Action> post, Action<Action> invoke, Func<bool> onUiThread, Func<Action, Action<bool>> ticker)
    {
        Stop();
        try
        {
            _bridge = new HandyBridge(post, invoke, onUiThread, ticker) { HideOverlay = true };
            _bridge.DictatingChanged += on => ListeningChanged?.Invoke(on);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Diag.Log("Handy: " + ex.Message); }
    }

    public void Stop()
    {
        _bridge?.Dispose();
        _bridge = null;
    }

    public bool OverlayOff()
    {
        try { return File.Exists(HandyBridge.SettingsPath) && HandyBridge.OverlayStyle(File.ReadAllText(HandyBridge.SettingsPath)) == "none"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public void Launch(string program)
    {
        if (Process.GetProcessesByName("handy").Any()) return;
        try { Process.Start(new ProcessStartInfo(program, "--start-hidden") { UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { Diag.Log("Handy didn't start: " + ex.Message); }
    }

    public async Task<bool> InstallAsync(CancellationToken ct)
    {
        // The Store app doesn't download and run other apps' installers: Holm opens Handy's release page instead.
        if (PackageIdentity.IsPackaged) return false;
        var (url, sha256) = await HandyBridge.FindInstallerUrlAsync(HandyHttp, ct) ?? throw new IOException("Handy's release has no Windows installer");
        string file = await HandyBridge.DownloadInstallerAsync(HandyHttp, url, sha256, ct);
        // The person goes through Handy's own installer; once it closes, Holm looks again.
        var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        if (installer != null) await installer.WaitForExitAsync(ct);
        return true;
    }
}
