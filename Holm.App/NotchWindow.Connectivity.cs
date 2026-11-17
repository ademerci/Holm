using System;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>"No internet" the moment it goes, "Back online · 3 min" when it returns (the WPF app's MainWindow.Connectivity.cs).</summary>
public sealed partial class NotchWindow
{
    const string OfflineGlyph = "", OnlineGlyph = "";
    static readonly IBrush Red = Brush("#FFFF453A");

    readonly Outage _outage = new();
    readonly DispatcherTimer _outageGrace = new() { Interval = Outage.Grace + TimeSpan.FromMilliseconds(100) };
    IConnectivityPlatform _connectivity = null!;

    void InitConnectivity(IPlatformServices platform)
    {
        _connectivity = platform.Connectivity;
        _connectivity.Changed += online => Dispatcher.UIThread.Post(() => OnConnectivityChanged(online));
        _outageGrace.Tick += (_, _) => { _outageGrace.Stop(); ConfirmOutage(); };
    }

    void StartConnectivity()
    {
        _outageGrace.Stop();
        _connectivity.Stop();
        if (_settings.Connectivity) _connectivity.Start();
    }

    void OnConnectivityChanged(bool online)
    {
        if (!_settings.Connectivity) return;
        Diag.Log(online ? "Internet back" : "Internet gone");
        if (!online)
        {
            _outage.Lost(DateTime.UtcNow);
            _outageGrace.Stop();
            _outageGrace.Start();
            return;
        }
        _outageGrace.Stop();
        if (_outage.Back(DateTime.UtcNow) is { } length) ShowPeek(OnlineGlyph, "Back online", "after " + Outage.Describe(length), null, seconds: 4, glyphBrush: Green);
    }

    /// <summary>Around sleep the network drops anyway: no outage until a little after waking.</summary>
    void HushConnectivity(TimeSpan forHowLong)
    {
        _outageGrace.Stop();
        _outage.Hush(DateTime.UtcNow + forHowLong);
    }

    /// <summary>As the WPF app's WM_POWERBROADCAST: quiet about the network while asleep, and catch up on waking.</summary>
    void OnSleep(bool going)
    {
        if (going)
        {
            HushConnectivity(TimeSpan.FromHours(24)); // until it wakes
            return;
        }
        Diag.Log("Woke up");
        HushConnectivity(TimeSpan.FromSeconds(45)); // Wi-Fi takes a moment to come back
        Dispatcher.UIThread.Post(CheckReminders, DispatcherPriority.Background); // anything due meanwhile
        Dispatcher.UIThread.Post(CheckDiskSoon, DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => _ = RefreshWeatherAsync(), DispatcherPriority.Background); // if half an hour has passed
    }

    void ConfirmOutage()
    {
        if (_connectivity.Online == false && _outage.StillLost(DateTime.UtcNow))
            ShowPeek(OfflineGlyph, "No internet", "", null, seconds: 5, glyphBrush: Red);
    }

    void SetConnectivity(bool on)
    {
        _settings.Connectivity = on;
        _settings.Save();
        StartConnectivity();
    }

    /// <summary>The connection now, as the speed test's page shows it.</summary>
    NetworkLink? CurrentLink()
        => _connectivity.CurrentLink() is { } link
            ? new NetworkLink(link.IsWifi, link.Ssid, link.SignalBars, link.LinkBitsPerSecond, Guid.TryParse(link.AdapterId, out var id) ? id : null)
            : null;
}
