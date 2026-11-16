using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Out of the way of games and fullscreen videos, and a small pill over a maximized browser so its tabs can be
/// clicked (Settings › Minimize over browser tabs): the WPF app's WatchFullscreen, asked every 1.5 s as it does.
/// </summary>
public sealed partial class NotchWindow
{
    /// <summary>Browser names as Linux and macOS give them, as the WPF app's Browsers list knows them.</summary>
    static readonly Dictionary<string, string> BrowserAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Google Chrome"] = "chrome", ["google-chrome"] = "chrome", ["chromium-browse"] = "chromium", ["firefox-bin"] = "firefox",
        ["Microsoft Edge"] = "msedge", ["microsoft-edge"] = "msedge", ["Brave Browser"] = "brave", ["brave-browser"] = "brave",
        ["Opera"] = "opera", ["Vivaldi"] = "vivaldi", ["Arc"] = "arc", ["Zen Browser"] = "zen",
        ["Safari"] = "chrome", // not on the WPF app's list (no Safari on Windows), but its tabs run along the top too
    };

    readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    IForegroundPlatform _foreground = null!;
    bool _hiddenForFullscreen, _browserInFront;

    void InitFullscreen(IPlatformServices platform)
    {
        _foreground = platform.Foreground;
        _watchTimer.Tick += (_, _) => WatchFullscreen();
    }

    void StartFullscreen() => _watchTimer.Start();

    internal static bool IsBrowser(string name) => Browsers.Is(BrowserAliases.TryGetValue(name, out var known) ? known : name);

    void WatchFullscreen()
    {
        if (ChosenScreen() is not { } screen) return;
        var front = _foreground.Read(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        bool wasFullscreen = _hiddenForFullscreen, wasBrowser = _browserInFront;
        _browserInFront = _settings.HideOverBrowsers && front.Maximized && IsBrowser(front.ProcessName);
        if (front.Fullscreen != _hiddenForFullscreen)
        {
            _hiddenForFullscreen = front.Fullscreen;
            if (front.Fullscreen)
            {
                _hovered = false;
                _peekActive = false;
                _live.Stop();
                ApplyState(animate: false);
                Hide();
            }
            else
            {
                Show();
                PositionWindow();
                if (TryGetPlatformHandle() is { } handle) _platform.MakeOverlay(handle.Handle);
                UpdateHitRegion(_shapeW, _shapeH, _shapeScale);
            }
            UpdateEqualizer();
        }
        if (_browserInFront != wasBrowser || (wasFullscreen && !front.Fullscreen)) ApplyState(animate: true);
        MarkFrontRead();
    }
}
