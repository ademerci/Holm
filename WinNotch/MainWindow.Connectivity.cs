using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>"No internet" the moment it goes, "Back online · 3 min" when it returns.</summary>
public partial class MainWindow
{
    const string OfflineGlyph = "", OnlineGlyph = "";
    readonly Outage _outage = new();
    NetworkWatcher? _network;
    DispatcherTimer _outageGrace = null!;

    void InitConnectivity() => _outageGrace = MakeTimer((int)Outage.Grace.TotalMilliseconds + 100, ConfirmOutage, oneShot: true);

    void StartConnectivity()
    {
        StopConnectivity();
        if (!_settings.Connectivity) return;
        try
        {
            _network = new NetworkWatcher(Dispatcher);
            _network.Changed += OnConnectivityChanged;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or NotSupportedException)
        {
            Diag.Log("Network list: " + ex.GetType().Name);
        }
    }

    void StopConnectivity()
    {
        _outageGrace.Stop();
        _network?.Dispose();
        _network = null;
    }

    void OnConnectivityChanged(bool online)
    {
        Diag.Log(online ? "Internet back" : "Internet gone");
        if (!online)
        {
            _outage.Lost(DateTime.UtcNow);
            _outageGrace.Stop();
            _outageGrace.Start();
            return;
        }
        _outageGrace.Stop();
        _ = RefreshWeatherAsync(); // if it's due: it may have been missed while offline
        if (_outage.Back(DateTime.UtcNow) is { } length) ShowPeek(OnlineGlyph, "Back online", "after " + Outage.Describe(length), null, false, GreenBrush, seconds: 4);
    }

    void ConfirmOutage()
    {
        if (_network?.Online == false && _outage.StillLost(DateTime.UtcNow))
            ShowPeek(OfflineGlyph, "No internet", "", null, false, null, seconds: 5, glyphBrush: RedBrush);
    }

    /// <summary>Around sleep the network drops anyway: no outage until a little after waking.</summary>
    void HushConnectivity(TimeSpan forHowLong)
    {
        _outageGrace.Stop();
        _outage.Hush(DateTime.UtcNow + forHowLong);
    }

    void MenuConnectivity_Click(object sender, RoutedEventArgs e)
    {
        _settings.Connectivity = MenuConnectivity.IsChecked;
        _settings.Save();
        StartConnectivity();
    }
}
