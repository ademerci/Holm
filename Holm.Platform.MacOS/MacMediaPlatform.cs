using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Spotify and Apple Music on macOS. macOS has no public API for "what is playing" across apps (the private one,
/// MediaRemote, refuses unsigned callers since macOS 15.4), so the two players are asked directly, through the
/// scripting both offer. Both announce every change with a distributed notification and macOS announces apps
/// starting and quitting; between those nothing is asked. The first time, macOS asks the user whether Holm may
/// control the player.
///
/// A player that isn't running is never addressed: talking to it would start it.
/// </summary>
public sealed class MacMediaPlatform : IMediaPlatform
{
    const int MaxCoverBytes = 8 * 1024 * 1024;
    const char Separator = (char)31;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6), MaxResponseContentBufferSize = MaxCoverBytes };
    static MacMediaPlatform? _current;
    static IntPtr _observer;

    internal sealed record Player(string BundleId, string Name, string ReadScript);

    internal static readonly Player Spotify = new("com.spotify.client", "Spotify", """
        tell application id "com.spotify.client"
            set s to player state as text
            if s is "stopped" then return "stopped"
            set t to current track
            set d to character id 31
            return s & d & (name of t) & d & (artist of t) & d & (album of t) & d & ((duration of t) / 1000) & d & player position & d & shuffling & d & repeating & d & (artwork url of t)
        end tell
        """);

    internal static readonly Player Music = new("com.apple.Music", "Music", """
        tell application id "com.apple.Music"
            set s to player state as text
            if s is "stopped" then return "stopped"
            try
                set t to current track
            on error
                return "stopped"
            end try
            set d to character id 31
            return s & d & (name of t) & d & (artist of t) & d & (album of t) & d & (duration of t) & d & player position & d & shuffle enabled & d & (song repeat as text)
        end tell
        """);

    static readonly Player[] Players = { Spotify, Music };

    readonly object _gate = new();
    bool _started;
    Player? _chosen;
    MediaTimeline? _timeline;
    string _coverKey = "", _artUrl = "";
    string? _cachedCoverKey;
    byte[]? _cover;

    public event Action<bool>? Changed;

    /// <summary>Called on the UI thread, which on macOS is the main thread, where the observers must be added.</summary>
    public Task StartAsync()
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _current = this;
        try { Observe(); } catch { }
        Changed?.Invoke(true);
        return Task.CompletedTask;
    }

    static unsafe void Observe()
    {
        if (_observer != IntPtr.Zero) return;
        IntPtr cls = ObjC.objc_allocateClassPair(ObjC.Class("NSObject"), "HolmMediaObserver", 0);
        if (cls == IntPtr.Zero) return;
        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> handler = &OnNotification;
        ObjC.class_addMethod(cls, ObjC.Sel("playerChanged:"), (IntPtr)handler, "v@:@");
        ObjC.objc_registerClassPair(cls);
        _observer = ObjC.Send(ObjC.Send(cls, ObjC.Sel("alloc")), ObjC.Sel("init"));

        IntPtr distributed = ObjC.Send(ObjC.Class("NSDistributedNotificationCenter"), ObjC.Sel("defaultCenter"));
        IntPtr add = ObjC.Sel("addObserver:selector:name:object:"), selector = ObjC.Sel("playerChanged:");
        foreach (var name in new[] { "com.spotify.client.PlaybackStateChanged", "com.apple.Music.playerInfo" })
            ObjC.Send(distributed, add, _observer, selector, ObjC.String(name), IntPtr.Zero);

        IntPtr workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Sel("sharedWorkspace"));
        IntPtr center = ObjC.Send(workspace, ObjC.Sel("notificationCenter"));
        foreach (var name in new[] { "NSWorkspaceDidLaunchApplicationNotification", "NSWorkspaceDidTerminateApplicationNotification" })
            ObjC.Send(center, add, _observer, selector, ObjC.String(name), IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    static void OnNotification(IntPtr self, IntPtr selector, IntPtr notification)
    {
        try { _current?.Changed?.Invoke(true); } catch { }
    }

    public Task<MediaState?> ReadAsync() => Task.Run(Read);

    MediaState? Read()
    {
        Player? chosen;
        lock (_gate) chosen = _chosen;

        // What plays wins; among several, the one already shown; with nothing playing, the one shown stays.
        (Player Player, MediaState State, string ArtUrl)? best = null;
        int bestRank = 0;
        foreach (var player in Players)
        {
            if (!IsRunning(player.BundleId)) continue;
            string? output = RunScript(player.ReadScript);
            if (output == null || Parse(output, player) is not { } parsed) continue;
            int rank = (parsed.State.IsPlaying ? 4 : 2) + (player == chosen ? 1 : 0);
            if (rank > bestRank) (best, bestRank) = ((player, parsed.State, parsed.ArtUrl), rank);
        }

        lock (_gate)
        {
            if (best is not { } found)
            {
                (_chosen, _timeline, _coverKey, _artUrl) = (null, null, "", "");
                return null;
            }
            _chosen = found.Player;
            _timeline = found.State.Timeline;
            _coverKey = found.Player.BundleId + Separator + found.State.Title + Separator + found.State.Album + Separator + found.State.Artist;
            _artUrl = found.ArtUrl;
            return found.State;
        }
    }

    /// <summary>The answer of a read script: "stopped", or the fields joined by <see cref="Separator"/>.</summary>
    internal static (MediaState State, string ArtUrl)? Parse(string output, Player player)
    {
        string[] f = output.TrimEnd('\r', '\n').Split(Separator);
        if (f.Length < 8 || f[1].Length == 0) return null;
        bool spotify = player == Spotify;
        var state = new MediaState
        {
            IsPlaying = f[0] == "playing",
            Title = f[1],
            Artist = f[2],
            Album = f[3],
            App = player.Name,
            AppId = player.BundleId,
            Shuffle = f[6] == "true",
            // Spotify only knows repeat on or off, which is its whole list.
            Repeat = spotify ? (f[7] == "true" ? 2 : 0) : f[7] switch { "one" => 1, "all" => 2, _ => 0 },
            CanShuffle = true, CanRepeat = true, CanSeek = true, CanPrevious = true, CanNext = true, CanPlayPause = true,
        };
        if (Seconds(f[4]) is > 0 and var duration && Seconds(f[5]) is { } position)
            state.Timeline = new MediaTimeline(TimeSpan.FromSeconds(Math.Clamp(position, 0, duration)), TimeSpan.FromSeconds(duration), DateTimeOffset.Now);
        return (state, spotify && f.Length > 8 ? f[8] : "");
    }

    /// <summary>A number as AppleScript writes it: with a point or the locale's comma, maybe as "2.15E+2".</summary>
    internal static double? Seconds(string text)
        => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;

    public MediaTimeline? ReadTimeline()
    {
        lock (_gate) return _timeline;
    }

    public async Task<byte[]?> ReadCoverAsync()
    {
        string key, url;
        Player? player;
        lock (_gate)
        {
            (key, url, player) = (_coverKey, _artUrl, _chosen);
            if (player == null || key.Length == 0) return null;
            // Asked again for the same track (the notch asks until it has one): no second fetch.
            if (key == _cachedCoverKey) return _cover;
        }
        byte[]? bytes = player == Spotify
            ? await DownloadAsync(url).ConfigureAwait(false)
            : await Task.Run(ReadMusicArtwork).ConfigureAwait(false);
        lock (_gate) (_cachedCoverKey, _cover) = (key, bytes);
        return bytes;
    }

    static async Task<byte[]?> DownloadAsync(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                return await Http.GetByteArrayAsync(uri).ConfigureAwait(false);
        }
        catch { }
        return null;
    }

    /// <summary>Music hands out the picture itself, which only a file can carry out of a script.</summary>
    static byte[]? ReadMusicArtwork()
    {
        string file = Path.Combine(Path.GetTempPath(), $"holm-cover-{Environment.ProcessId}.bin");
        try
        {
            File.Delete(file);
            string script = $$"""
                tell application id "com.apple.Music"
                    try
                        set r to raw data of artwork 1 of current track
                    on error
                        return ""
                    end try
                end tell
                set f to open for access (POSIX file "{{file}}") with write permission
                set eof f to 0
                write r to f
                close access f
                return "ok"
                """;
            if (RunScript(script)?.Trim() != "ok") return null;
            var info = new FileInfo(file);
            return info.Exists && info.Length > 0 && info.Length <= MaxCoverBytes ? File.ReadAllBytes(file) : null;
        }
        catch { return null; }
        finally { try { File.Delete(file); } catch { } }
    }

    public void PlayPause() => Command("playpause");
    public void Play() => Command("play");
    public void Pause() => Command("pause");
    public void Next() => Command("next track");
    public void Previous() => Command("previous track");

    public void Seek(TimeSpan position)
        => Command("set player position to " + Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture));

    public void SetShuffle(bool on)
        => Command(Chosen() == Spotify ? $"set shuffling to {(on ? "true" : "false")}" : $"set shuffle enabled to {(on ? "true" : "false")}");

    public void SetRepeat(int mode)
        => Command(Chosen() == Spotify
            ? $"set repeating to {(mode != 0 ? "true" : "false")}"
            : "set song repeat to " + mode switch { 1 => "one", 2 => "all", _ => "off" });

    Player? Chosen()
    {
        lock (_gate) return _chosen;
    }

    void Command(string command)
    {
        if (Chosen() is not { } player) return;
        string script = $"tell application id \"{player.BundleId}\" to {command}";
        _ = Task.Run(() =>
        {
            if (IsRunning(player.BundleId)) RunScript(script);
            // Spotify doesn't announce every change (shuffle, repeat, a seek): read again either way.
            Changed?.Invoke(true);
        });
    }

    /// <summary>Whether an app is running, without starting it.</summary>
    static bool IsRunning(string bundleId)
    {
        IntPtr pool = ObjC.objc_autoreleasePoolPush();
        try
        {
            IntPtr apps = ObjC.Send(ObjC.Class("NSRunningApplication"), ObjC.Sel("runningApplicationsWithBundleIdentifier:"), ObjC.String(bundleId));
            return apps != IntPtr.Zero && ObjC.SendReturnLong(apps, ObjC.Sel("count")) > 0;
        }
        catch { return false; }
        finally { ObjC.objc_autoreleasePoolPop(pool); }
    }

    /// <summary>Runs an AppleScript; null when it fails or takes too long.</summary>
    static string? RunScript(string script)
    {
        try
        {
            var start = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(script);
            using var process = Process.Start(start);
            if (process == null) return null;
            var output = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000)) { try { process.Kill(); } catch { } return null; }
            return process.ExitCode == 0 ? output.Result : null;
        }
        catch { return null; }
    }
}
