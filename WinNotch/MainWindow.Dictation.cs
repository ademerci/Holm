using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinNotch;

/// <summary>
/// Dictation with Handy (github.com/cjpais/Handy, free, offline, MIT-licensed by CJ Pais): while Handy listens, the notch
/// says so in place of Handy's own pink overlay, which Holm keeps out of sight. Holm hooks into Handy if it's installed,
/// and otherwise offers to fetch Handy's official installer from its GitHub releases.
/// </summary>
public partial class MainWindow
{
    HandyBridge? _handy;
    bool _dictating;
    static readonly HttpClient HandyHttp = new() { DefaultRequestHeaders = { { "User-Agent", "Holm" } } };

    void StartDictation()
    {
        StopDictation();
        if (!_settings.Dictation) return;
        if (HandyBridge.FindInstall() is not { } exe)
        {
            OfferHandy();
            return;
        }
        try
        {
            _handy = new HandyBridge(Dispatcher) { HideOverlay = true };
            _handy.DictatingChanged += OnDictating;
            OnDictating(_handy.IsDictating);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Diag.Log("Handy: " + ex.Message); }
        // Without its overlay Handy shows nothing a window can see, so the notch couldn't either.
        try
        {
            if (File.Exists(HandyBridge.SettingsPath) && HandyBridge.OverlayStyle(File.ReadAllText(HandyBridge.SettingsPath)) == "none")
                ShowPeek("", "Turn Handy's overlay on", "Handy › Settings", null, false, null, seconds: 5);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        // Handy runs in the tray; start it (hidden) if it isn't running yet.
        if (!Process.GetProcessesByName("handy").Any())
        {
            try { Process.Start(new ProcessStartInfo(exe, "--start-hidden") { UseShellExecute = false }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { Diag.Log("Handy didn't start: " + ex.Message); }
        }
    }

    void StopDictation()
    {
        if (_handy != null)
        {
            _handy.DictatingChanged -= OnDictating;
            _handy.Dispose();
            _handy = null;
        }
        if (_dictating) OnDictating(false);
    }

    void OnDictating(bool on)
    {
        if (on == _dictating) return;
        _dictating = on;
        // A calm breath while it listens: about one a second, never on with animations off.
        var opacity = on && _settings.Animations
            ? new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(550)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }
            : null;
        DictationDot.BeginAnimation(OpacityProperty, opacity);
        ApplyState();
    }

    /// <summary>Handy isn't on this PC: offer its official installer (the person installs it, Holm only fetches it).</summary>
    void OfferHandy()
    {
        if (_busy) return;
        ShowTask("", ShareBlue, "Dictation needs Handy", "Free and offline, by CJ Pais (MIT). Get it from its GitHub releases?", null,
                 ("Get Handy", () => _ = InstallHandyAsync()), ("Not now", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(15);
        _taskHide.Start();
    }

    async Task InstallHandyAsync()
    {
        var cts = BeginTask();
        ShowTask("", ShareBlue, "Getting Handy…", "From github.com/cjpais/Handy", null, null, ("Cancel", CancelTask));
        try
        {
            var (url, sha256) = await HandyBridge.FindInstallerUrlAsync(HandyHttp, cts.Token) ?? throw new IOException("Handy's release has no Windows installer");
            string file = await HandyBridge.DownloadInstallerAsync(HandyHttp, url, sha256, cts.Token);
            EndTask("", ShareBlue, "Handy's installer is open", "Holm hooks in once it's installed");
            // The person goes through Handy's own installer; once it closes, Holm looks again.
            var installer = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            if (installer != null) await installer.WaitForExitAsync();
            if (_settings.Dictation && HandyBridge.FindInstall() != null) StartDictation();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Fail(ex, cts, "Couldn't get Handy");
        }
    }

    void MenuDictation_Click(object sender, RoutedEventArgs e)
    {
        _settings.Dictation = MenuDictation.IsChecked;
        _settings.Save();
        StartDictation();
    }
}
