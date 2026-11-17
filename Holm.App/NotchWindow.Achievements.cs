using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// Achievements (the WPF app's MainWindow.Achievements.cs): the notch counts what you do with it and pops out a
/// trophy when one unlocks; friends see only how many you have. And waving at a friend (MainWindow.Wave.cs): a note
/// that pops up on their notch with "Wave back", at most once every 30 seconds per friend.
/// </summary>
public sealed partial class NotchWindow
{
    AchievementBook _achievements = null!;

    void InitAchievements()
    {
        // Under the test runner the notch is a test window: its progress mustn't reach your achievements.
        bool test = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
        _achievements = new AchievementBook(test ? null : Path.Combine(Diag.Folder, "achievements.json"));
        _friends.MyTrophies = () => _settings.ShareActivity ? _achievements.UnlockedCount : null;
        _friends.ListChanged += () => _achievements.Set(AchievementBook.Friends, _friends.Friends.Count);
        // Unlocks that were reached before achievements existed come quietly.
        _achievements.Set(AchievementBook.Friends, _friends.Friends.Count);
        if (_settings.ProfilePicture != null) _achievements.Set(AchievementBook.ProfilePicture, 1);
        _achievements.Unlocked += OnUnlocked;
        _achievements.Used(DateTime.Now);
        _friends.Waved += OnWaved;
    }

    /// <summary>Something that counts toward an achievement happened.</summary>
    void Achieve(string counter, long by = 1)
    {
        if (_achievements == null || AppSettings.ReadOnly) return; // a self-test counts nothing
        _achievements.Add(counter, by);
    }

    /// <summary>Files arrived at the other end: they count, and so does their size.</summary>
    void CountSent(IEnumerable<string> paths)
    {
        long bytes = 0, files = 0;
        foreach (string path in paths)
        {
            files++;
            try { if (File.Exists(path)) bytes += new FileInfo(path).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Achieve(AchievementBook.FilesSent, files);
        if (bytes >= 1 << 20) Achieve(AchievementBook.MegabytesSent, bytes >> 20);
    }

    static IBrush ColorOf(Achievement achievement) => new SolidColorBrush(Color.FromUInt32(achievement.Argb)).ToImmutable();

    void OnUnlocked(IReadOnlyList<Achievement> unlocked)
    {
        Diag.Log($"Achievement unlocked: {string.Join(", ", unlocked.Select(a => a.Id))}");
        _settingsWindow?.RefreshBadges();
        if (!_settings.AchievementPopups) return;
        var first = unlocked[0];
        string title = unlocked.Count == 1 ? "Achievement unlocked" : $"{unlocked.Count} achievements unlocked";
        string subtitle = $"{first.Title} · {first.Description}";
        if (_busy || _taskVisible || _sharing || _hiddenForFullscreen)
        {
            ShowPeek(first.Glyph, title, first.Title, null, seconds: 5, glyphBrush: ColorOf(first));
            return;
        }
        ShowTask(first.Glyph, ColorOf(first), title, subtitle, null, ("Show", () => { HideTask(); ShowHub("achievements"); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    // ───────────── waves ─────────────

    void OnWaved(Friend friend)
    {
        Diag.Log("A friend waved");
        if (_busy || _taskVisible || _sharing || _hiddenForFullscreen)
        {
            ShowPeek(null, $"{friend.Name} waves at you", "", null, seconds: 4);
            return;
        }
        ShowTask(FaceText(friend.Name), FaceBrush(friend.Name), $"{friend.Name} waves at you", "Just saying hi", null,
                 ("Wave back", () => { HideTask(); WaveAt(friend); }), ("OK", HideTask), textIcon: true);
        _taskHide.Interval = TimeSpan.FromSeconds(12);
        _taskHide.Start();
    }

    void WaveAt(Friend friend)
    {
        if (_friends.Wave(friend))
        {
            ShowPeek(null, $"You waved at {friend.Name}", "", null);
            Achieve(AchievementBook.Waves);
        }
        else ShowPeek(null, $"You just waved at {friend.Name}", $"again in {Math.Ceiling(_friends.WaveWait(friend).TotalSeconds)} s", Dim);
    }
}

/// <summary>
/// Your activity for friends (music, a call, a game, a timer), like a status in a chat app (the WPF app's
/// MainWindow.Activity.cs). It goes only to friends whose Holm asks whether yours runs, and only while "Share my
/// activity" is on.
/// </summary>
public sealed partial class NotchWindow
{
    static readonly string[] VideoPlayers = { "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "PotPlayerMini", "PotPlayerMini64", "wmplayer", "Video.UI", "mpv", "Netflix", "ApplicationFrameHost", "totem", "celluloid", "smplayer" };

    FriendActivity? _activity;

    void InitActivity() => _friends.MyActivity = () => Dispatcher.UIThread.CheckAccess() ? MyActivity() : Dispatcher.UIThread.Invoke(MyActivity);

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

    /// <summary>A game: a fullscreen window in front that isn't a browser or a video player.</summary>
    string? GameInFront()
    {
        if (ChosenScreen() is not { } screen) return null;
        var front = _foreground.Read(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height);
        string exe = front.ProcessName;
        if (!front.Fullscreen || exe.Length == 0 || IsBrowser(exe) || VideoPlayers.Contains(exe, StringComparer.OrdinalIgnoreCase)) return null;
        return char.ToUpper(exe[0]) + exe[1..];
    }

    void SetShareActivity(bool on)
    {
        _settings.ShareActivity = on;
        _settings.Save();
        _social?.Raise();
    }
}

/// <summary>
/// Your profile (picture, name, status, color), edited in the Holm window and sent to your friends (the WPF app's
/// MainWindow.Profile.cs).
/// </summary>
public sealed partial class NotchWindow
{
    ProfileDraft CurrentProfile() => new(_settings.ProfileName, _settings.ProfileStatus, _settings.ProfileColor, _settings.ProfilePicture);

    void ApplyProfile(ProfileDraft profile)
    {
        string oldName = _friends.MyName;
        _settings.ProfileName = FriendProfile.CleanText(profile.Name, FriendProfile.MaxName);
        _settings.ProfileStatus = FriendProfile.CleanText(profile.Status, FriendProfile.MaxStatus);
        _settings.ProfileColor = FriendProfile.CleanColor(profile.Color);
        _settings.ProfilePicture = profile.Picture;
        _settings.Save();
        if (profile.Picture != null) _achievements.Set(AchievementBook.ProfilePicture, 1);
        _friends.ProfileChanged();
        if (_friends.MyName != oldName && _nearby != null)
        {
            // People nearby see the name the network service started with: start it again with the new one.
            _nearby.Dispose();
            _nearby = null;
            StartSharing();
        }
        Diag.Log("Profile saved");
    }

    /// <summary>Your face, name and status for the top of the Holm window's sidebar.</summary>
    Avalonia.Controls.Control MeCard(ProfileDraft profile)
    {
        string name = FriendProfile.CleanText(profile.Name, FriendProfile.MaxName) ?? FriendsService.AccountName();
        var picture = ProfileEditor.PictureBrush(profile.Picture);
        IBrush fill = picture ?? (FriendProfile.CleanColor(profile.Color) is { } color && Color.TryParse(color, out var c) ? new SolidColorBrush(c) : AvatarColor(name));
        var row = new Avalonia.Controls.Grid { ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("Auto,*") };
        row.Children.Add(HubTheme.Face(picture != null ? "" : Initials(name), fill, 36));
        var words = new Avalonia.Controls.StackPanel { Margin = new Avalonia.Thickness(10, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        words.Children.Add(new Avalonia.Controls.TextBlock { Text = name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new Avalonia.Controls.TextBlock
        {
            Text = FriendProfile.CleanText(profile.Status, FriendProfile.MaxStatus) ?? "Set a status", FontSize = 11,
            Foreground = HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Avalonia.Controls.Grid.SetColumn(words, 1);
        row.Children.Add(words);
        return row;
    }
}
