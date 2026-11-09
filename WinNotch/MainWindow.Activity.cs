using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Your activity for friends (music, a call, a game, a timer), like a status in a chat app. It goes only to friends
/// whose Holm asks whether yours runs, and only while "Share my activity" is on.
/// </summary>
public partial class MainWindow
{
    static readonly string[] VideoPlayers = { "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "PotPlayerMini", "PotPlayerMini64", "wmplayer", "Video.UI", "mpv", "Netflix", "ApplicationFrameHost" };

    FriendActivity? _activity;

    void InitActivity()
    {
        _friends.MyActivity = MyActivity;
        _friends.ActivityChanged += _ => _social?.Raise();
        _friends.FriendOnline += _ => _social?.Raise();
        _friends.ProfileReceived += _ => _social?.Raise();
    }

    /// <summary>What we're doing now; the same activity keeps the time it began.</summary>
    FriendActivity? MyActivity()
    {
        if (!_settings.ShareActivity) return null;
        var now = CurrentActivity();
        if (now == null) return _activity = null;
        if (_activity != null && _activity.Kind == now.Kind && _activity.Title == now.Title)
            return _activity = now with { Since = _activity.Since };
        return _activity = now;
    }

    /// <summary>The one thing that says most: a stream, then a call, a game, music, a timer.</summary>
    FriendActivity? CurrentActivity()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (StreamingTo() is { } audience) return new FriendActivity(FriendActivity.Stream, "Streaming their screen", audience, now);
        if (_call is { } call) return new FriendActivity(FriendActivity.Call, "In a call", call.Name, now);
        if (GameInFront() is { } game) return new FriendActivity(FriendActivity.Game, game, null, now);
        if (_info is { IsPlaying: true } media && media.Title.Length > 0)
            return new FriendActivity(FriendActivity.Music, media.Title, media.Artist.Length > 0 ? media.Artist : media.App, now);
        if (_timer is { Paused: false } timer)
            return new FriendActivity(FriendActivity.Timer, "On a timer", Countdown.Format(timer.Remaining(DateTime.UtcNow)) + " left", now);
        return null;
    }

    /// <summary>A game in fullscreen in front: Windows says a Direct3D app has the screen, or a borderless one that isn't a browser or video player.</summary>
    static string? GameInFront()
    {
        if (Native.SHQueryUserNotificationState(out int state) != 0 || state is not (2 or 3)) return null;
        IntPtr window = Native.GetForegroundWindow();
        string exe = Native.ProcessNameOf(window);
        if (exe.Length == 0 || Browsers.Is(exe) || VideoPlayers.Contains(exe, StringComparer.OrdinalIgnoreCase)) return null;
        if (state == 2 && exe.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return null;
        // The name the game gives itself ("VALORANT"), else its program's name.
        try
        {
            if (Native.ProcessPathOf(window) is { } path && FileVersionInfo.GetVersionInfo(path) is { FileDescription: { Length: > 0 } name })
                return name.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or IOException) { }
        return char.ToUpper(exe[0]) + exe[1..];
    }

    /// <summary>Who we stream to, while streaming (see the streaming feature); null otherwise.</summary>
    string? StreamingTo() => null;

    void MenuShareActivity_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShareActivity = MenuShareActivity.IsChecked;
        _settings.Save();
    }
}
