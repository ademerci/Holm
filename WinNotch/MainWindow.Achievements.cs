using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WinNotch;

/// <summary>
/// Achievements: the notch counts what you do with it and pops out a trophy when one unlocks. Friends see only how
/// many you have, in the notes that tell them you're online.
/// </summary>
public partial class MainWindow
{
    AchievementBook _achievements = null!;

    void InitAchievements()
    {
        // Under the test runner the notch is a test window: its progress mustn't reach your achievements.
        bool test = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "").StartsWith("testhost", StringComparison.OrdinalIgnoreCase);
        _achievements = new AchievementBook(test ? null : System.IO.Path.Combine(Diag.Folder, "achievements.json"));
        _friends.MyTrophies = () => _settings.ShareActivity ? _achievements.UnlockedCount : null;
        _friends.ListChanged += () => _achievements.Set(AchievementBook.Friends, _friends.Friends.Count);
        // Unlocks that were reached before achievements existed come quietly.
        _achievements.Set(AchievementBook.Friends, _friends.Friends.Count);
        if (_settings.ProfilePicture != null) _achievements.Set(AchievementBook.ProfilePicture, 1);
        _achievements.Unlocked += OnUnlocked;
        _achievements.Used(DateTime.Now);
    }

    /// <summary>Something that counts toward an achievement happened.</summary>
    void Achieve(string counter, long by = 1)
    {
        if (_achievements == null || AppSettings.ReadOnly) return; // snapshots count nothing
        _achievements.Add(counter, by);
    }

    /// <summary>Files arrived at the other end: they count, and so does their size.</summary>
    void CountSent(System.Collections.Generic.IEnumerable<string> paths)
    {
        long bytes = 0, files = 0;
        foreach (string path in paths)
        {
            files++;
            try { if (System.IO.File.Exists(path)) bytes += new System.IO.FileInfo(path).Length; }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        }
        Achieve(AchievementBook.FilesSent, files);
        if (bytes >= 1 << 20) Achieve(AchievementBook.MegabytesSent, bytes >> 20);
    }

    void OnUnlocked(IReadOnlyList<Achievement> unlocked)
    {
        Diag.Log($"Achievement unlocked: {string.Join(", ", System.Linq.Enumerable.Select(unlocked, a => a.Id))}");
        _settingsWindow?.RefreshBadges();
        if (!_settings.AchievementPopups) return;
        var first = unlocked[0];
        string title = unlocked.Count == 1 ? "Achievement unlocked" : $"{unlocked.Count} achievements unlocked";
        string subtitle = $"{first.Title} · {first.Description}";
        if (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen)
        {
            ShowPeek(first.Glyph, title, first.Title, null, false, Frozen(first.Color), seconds: 5);
            return;
        }
        ShowTask(first.Glyph, Frozen(first.Color), title, subtitle, null, ("Show", () => { HideTask(); ShowHub("achievements"); }), ("OK", HideTask));
        _taskHide.Interval = TimeSpan.FromSeconds(8);
        _taskHide.Start();
    }

    void MenuAchievementPopups_Click(object sender, RoutedEventArgs e)
    {
        _settings.AchievementPopups = MenuAchievementPopups.IsChecked;
        _settings.Save();
    }
}
