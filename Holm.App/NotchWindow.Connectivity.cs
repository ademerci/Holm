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
}
