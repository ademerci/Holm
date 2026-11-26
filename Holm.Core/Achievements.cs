using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WinNotch;

/// <summary>One achievement: unlocked once <see cref="Counter"/> reaches <see cref="Goal"/>.</summary>
/// <param name="Argb">Its colour as 0xAARRGGBB (the WPF side makes a Color of it: Achievements.Wpf.cs).</param>
internal sealed partial record Achievement(string Id, string Title, string Description, string Glyph, uint Argb, string Counter, long Goal);

/// <summary>
/// Achievements for using Holm: counters that go up as things happen (a file sent, a timer done, a day in a row) and
/// the achievements they unlock. Kept in achievements.json on this PC, apart from the settings; friends only see how
/// many you have.
/// </summary>
internal sealed class AchievementBook
{
    // Counters
    public const string Friends = "friends", FilesSent = "files-sent", MegabytesSent = "mb-sent", Messages = "messages",
        Waves = "waves", Streams = "streams", StreamMinutes = "stream-minutes", Watched = "watched", Timers = "timers",
        Screenshots = "screenshots", Colors = "colors", Reminders = "reminders", Songs = "songs", ProfilePicture = "profile-picture",
        Night = "night", Streak = "streak", Notes = "notes", Verity = "verity";

    const uint Blue = 0xFF0A84FF, Pink = 0xFFFF375F, Green = 0xFF30D158, Purple = 0xFF5E5CE6, Orange = 0xFFFF9F0A, Teal = 0xFF32ADE6,
        Red = 0xFFFF453A, Gold = 0xFFFFC43D;

    public static readonly IReadOnlyList<Achievement> All = new Achievement[]
    {
        new("friend-1", "Better together", "Add your first friend", "", Pink, Friends, 1),
        new("friends-5", "Squad", "Have five friends", "", Pink, Friends, 5),
        new("beam-1", "Beam me up", "Send your first file", "", Blue, FilesSent, 1),
        new("beam-25", "Courier", "Send 25 files", "", Blue, FilesSent, 25),
        new("beam-gb", "Heavy lifter", "Send 1 GB in total", "", Blue, MegabytesSent, 1024),
        new("chat-1", "Say hi", "Send your first chat message", "", Green, Messages, 1),
        new("chat-100", "Chatterbox", "Send 100 chat messages", "", Green, Messages, 100),
        new("wave-1", "Hello there", "Wave at a friend", "", Green, Waves, 1),
        new("stream-1", "Going live", "Stream your screen to a friend", "", Red, Streams, 1),
        new("stream-60", "Broadcaster", "Stream for an hour in total", "", Red, StreamMinutes, 60),
        new("watch-1", "Front row", "Watch a friend's stream", "", Red, Watched, 1),
        new("timer-10", "On the clock", "Finish 10 timers", "", Orange, Timers, 10),
        new("reminder-10", "Never forget", "Have 10 reminders ring on time", "", Orange, Reminders, 10),
        new("notes-1", "Note to self", "Write a note in the notch", "", Orange, Notes, 1),
        new("shots-25", "Snap happy", "Take 25 screenshots", "", Teal, Screenshots, 25),
        new("color-10", "Color nerd", "Pick 10 colors from the screen", "", Teal, Colors, 10),
        new("songs-100", "Soundtrack", "Play 100 songs", "", Purple, Songs, 100),
        new("profile", "That's me", "Give yourself a profile picture", "", Purple, ProfilePicture, 1),
        new("night-owl", "Night owl", "Use Holm between 2 and 4 at night", "", Purple, Night, 1),
        new("streak-7", "Regular", "Use Holm 7 days in a row", "", Gold, Streak, 7),
        new("streak-30", "Part of the furniture", "Use Holm 30 days in a row", "", Gold, Streak, 30),
        new("verity", "Verity", "Invite Verity into your notch on Settings › General", "", Gold, Verity, 1),
    };

    readonly string? _path;
    readonly Data _data = new();

    /// <summary>Achievements that were just unlocked (one call can unlock several).</summary>
    public event Action<IReadOnlyList<Achievement>>? Unlocked;

    /// <param name="path">Where progress is kept; null keeps it in memory (snapshots, tests).</param>
    public AchievementBook(string? path)
    {
        _path = path;
        if (path == null || !File.Exists(path)) return;
        try { _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path)) ?? new Data(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Diag.Log("Achievements unreadable, starting over: " + ex.Message);
        }
    }

    /// <summary>Progress as given, kept in memory only (for the snapshots).</summary>
    internal AchievementBook(IDictionary<string, long> counters, IDictionary<string, long> unlocked) : this((string?)null)
    {
        foreach (var (k, v) in counters) _data.Counters[k] = v;
        foreach (var (k, v) in unlocked) _data.Unlocked[k] = v;
    }

    public long Value(string counter) => _data.Counters.TryGetValue(counter, out long v) ? v : 0;

    public bool IsUnlocked(Achievement achievement) => _data.Unlocked.ContainsKey(achievement.Id);

    public DateTimeOffset? UnlockedAt(Achievement achievement)
        => _data.Unlocked.TryGetValue(achievement.Id, out long at) ? DateTimeOffset.FromUnixTimeSeconds(at) : null;

    public int UnlockedCount => All.Count(IsUnlocked);

    /// <summary>Adds to a counter (a file sent, a song played).</summary>
    public IReadOnlyList<Achievement> Add(string counter, long by = 1) => Set(counter, Value(counter) + by);

    /// <summary>Sets a counter to how things stand (friends you have, days in a row).</summary>
    public IReadOnlyList<Achievement> Set(string counter, long value)
    {
        if (Value(counter) == value) return Array.Empty<Achievement>();
        _data.Counters[counter] = value;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var unlocked = All.Where(a => a.Counter == counter && value >= a.Goal && !IsUnlocked(a)).ToList();
        foreach (var achievement in unlocked) _data.Unlocked[achievement.Id] = now;
        Save();
        if (unlocked.Count > 0) Unlocked?.Invoke(unlocked);
        return unlocked;
    }

    /// <summary>Holm was used: counts the days in a row, and the night owl between 2 and 4.</summary>
    public void Used(DateTime localNow)
    {
        string today = localNow.ToString("yyyy-MM-dd");
        if (_data.StreakDay != today)
        {
            string yesterday = localNow.AddDays(-1).ToString("yyyy-MM-dd");
            long streak = _data.StreakDay == yesterday ? Value(Streak) + 1 : 1;
            _data.StreakDay = today;
            Set(Streak, streak);
            Save(); // the day, even when the count stays the same
        }
        if (localNow.Hour is 2 or 3 && Value(Night) == 0) Set(Night, 1);
    }

    /// <summary>Progress changed (saved), for the Holm account to sync.</summary>
    public event Action? Changed;

    /// <summary>Progress as the Holm account keeps it.</summary>
    internal Account.AchievementsPayload Export() => new()
    {
        Counters = new Dictionary<string, long>(_data.Counters, StringComparer.Ordinal),
        Unlocked = new Dictionary<string, long>(_data.Unlocked, StringComparer.Ordinal),
        StreakDay = _data.StreakDay,
    };

    /// <summary>
    /// Takes the account's merged progress (Account.AchievementsSync.Merge). What another PC unlocked arrives quietly:
    /// no trophy pops out for it here. True if anything changed.
    /// </summary>
    internal bool Import(Account.AchievementsPayload merged)
    {
        bool changed = merged.StreakDay != _data.StreakDay
                       || merged.Counters.Count != _data.Counters.Count || merged.Counters.Any(c => Value(c.Key) != c.Value)
                       || merged.Unlocked.Count != _data.Unlocked.Count
                       || merged.Unlocked.Any(u => !_data.Unlocked.TryGetValue(u.Key, out long at) || at != u.Value);
        if (!changed) return false;
        foreach (var (k, v) in merged.Counters) _data.Counters[k] = v;
        foreach (var (k, v) in merged.Unlocked) _data.Unlocked[k] = v;
        _data.StreakDay = merged.StreakDay;
        Save();
        return true;
    }

    void Save()
    {
        if (_path == null || AppSettings.ReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_data));
            File.Move(temp, _path, overwrite: true);
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Diag.Log("Couldn't save achievements: " + ex.Message); }
    }

    sealed class Data
    {
        public Dictionary<string, long> Counters { get; set; } = new();
        public Dictionary<string, long> Unlocked { get; set; } = new();
        /// <summary>The last day Holm was used ("yyyy-MM-dd"), for days in a row.</summary>
        public string? StreakDay { get; set; }
    }
}
