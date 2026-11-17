using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Dictation with Handy (github.com/cjpais/Handy, free, offline, MIT-licensed by CJ Pais), as the WPF app's
/// MainWindow.Dictation.cs: while Handy listens, the notch says so. Holm hooks into Handy if it's installed, and
/// otherwise offers to fetch its official installer (Windows) or opens its release page (Linux).
/// </summary>
public sealed partial class NotchWindow
{
    const string HandyRelease = "https://github.com/cjpais/Handy/releases/latest";
    IDictationPlatform _dictation = null!;
    bool _dictating;
    readonly Grid _dictationView = new() { Margin = new Thickness(14, 0) };
    readonly Ellipse _dictationDot = new() { Width = 8, Height = 8, Fill = Brush("#FF9B8CFF"), VerticalAlignment = VerticalAlignment.Center };

    void InitDictation(IPlatformServices platform)
    {
        _dictation = platform.Dictation;
        _dictation.ListeningChanged += OnDictating;
        var left = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(_dictationDot);
        left.Children.Add(new TextBlock { Text = MicGlyph, FontFamily = IconFont, FontSize = 13, Foreground = Brush("#FF9B8CFF"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _dictationView.Children.Add(left);
        _dictationView.Children.Add(new TextBlock
        {
            Text = "Listening", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush("#FFC9C1FF"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        });
        _dictationView.Opacity = 0;
        _dictationView.IsHitTestVisible = false;
        _islandContent.Children.Add(_dictationView);
    }

    void StartDictation()
    {
        StopDictation();
        if (!_settings.Dictation) return;
        if (_dictation.FindHandy() is not { } program)
        {
            OfferHandy();
            return;
        }
        _dictation.Start(action => Dispatcher.UIThread.Post(action), action => Dispatcher.UIThread.Invoke(action), Dispatcher.UIThread.CheckAccess, update =>
        {
            var timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => update());
            timer.Stop();
            return on => { if (on) timer.Start(); else timer.Stop(); };
        });
        // Without its overlay Handy shows nothing a window can see, so the notch couldn't either.
        if (_dictation.OverlayOff()) ShowPeek(MicGlyph, "Turn Handy's overlay on", "Handy › Settings", null, seconds: 5);
        // Handy runs in the tray; start it (hidden) if it isn't running yet.
        _dictation.Launch(program);
        if (!_dictation.ReportsListening) OnDictating(HandyListens());
    }

    void StopDictation()
    {
        _dictation.Stop();
        if (_dictating) OnDictating(false);
    }

    /// <summary>Where the backend can't see Handy's overlay: Handy recording from the microphone means it listens.</summary>
    bool HandyListens() => _settings.Dictation && AllSensors.Any(u => u.Sensor == Sensor.Microphone && IsHandy(u));

    static bool IsHandy(SensorUse use)
        => use.Name.Equals("handy", StringComparison.OrdinalIgnoreCase) || use.Key.Equals("handy", StringComparison.OrdinalIgnoreCase)
           || use.Key.EndsWith("handy.exe", StringComparison.OrdinalIgnoreCase);

    void OnDictating(bool on)
    {
        if (on == _dictating) return;
        _dictating = on;
        // A calm breath while it listens: about one a second, never with animations off.
        if (on && _settings.Animations) Breathe();
        else { StopTween(_dictationDot, "breath"); _dictationDot.Opacity = 1; }
        ApplyState(animate: true);
    }

    void Breathe()
    {
        if (!_dictating || !_settings.Animations) return;
        bool down = _dictationDot.Opacity > 0.7;
        Animate(_dictationDot, "breath", v => _dictationDot.Opacity = v, _dictationDot.Opacity, down ? 0.35 : 1, 550, 0,
                t => (1 - Math.Cos(t * Math.PI)) / 2, Breathe);
    }

    /// <summary>Handy isn't on this computer: offer its official installer (the person installs it, Holm only fetches it).</summary>
    void OfferHandy()
    {
        if (_busy) return;
        ShowTask(MicGlyph, ShareBlue, "Dictation needs Handy", "Free and offline, by CJ Pais (MIT). Get it from its GitHub releases?", null,
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
            if (!await _dictation.InstallAsync(cts.Token))
            {
                // Linux (a package or an AppImage, the person picks) and the Store app: Handy's release page.
                _shell.Open(HandyRelease);
                EndTask(MicGlyph, ShareBlue, "Handy's release page is open", "Holm hooks in once it's installed");
                return;
            }
            EndTask(MicGlyph, ShareBlue, "Handy's installer is open", "Holm hooks in once it's installed");
            if (_settings.Dictation && _dictation.FindHandy() != null) StartDictation();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or System.IO.InvalidDataException
                                       or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Fail(ex, cts, "Couldn't get Handy");
        }
    }

    void SetDictation(bool on)
    {
        _settings.Dictation = on;
        _settings.Save();
        StartDictation();
    }
}
