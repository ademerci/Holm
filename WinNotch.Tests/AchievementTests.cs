using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class AchievementTests
{
    [Fact]
    public void An_achievement_unlocks_once_its_goal_is_reached_and_only_once()
    {
        var book = new AchievementBook((string?)null);
        var heard = new List<string>();
        book.Unlocked += list => heard.AddRange(list.Select(a => a.Id));

        Assert.Equal(new[] { "beam-1" }, book.Add(AchievementBook.FilesSent).Select(a => a.Id));
        Assert.Empty(book.Add(AchievementBook.FilesSent, 10));
        Assert.Equal(new[] { "beam-25" }, book.Add(AchievementBook.FilesSent, 20).Select(a => a.Id));
        Assert.Empty(book.Add(AchievementBook.FilesSent, 100));
        Assert.Equal(new[] { "beam-1", "beam-25" }, heard);
        Assert.Equal(2, book.UnlockedCount);
    }

    [Fact]
    public void One_jump_can_unlock_several()
    {
        var book = new AchievementBook((string?)null);
        Assert.Equal(new[] { "friend-1", "friends-5" }, book.Set(AchievementBook.Friends, 6).Select(a => a.Id));
    }

    [Fact]
    public void An_unlocked_achievement_stays_when_the_counter_drops()
    {
        var book = new AchievementBook((string?)null);
        book.Set(AchievementBook.Friends, 1);
        book.Set(AchievementBook.Friends, 0); // the friend was removed
        Assert.True(book.IsUnlocked(AchievementBook.All.Single(a => a.Id == "friend-1")));
    }

    [Fact]
    public void Days_in_a_row_count_up_and_start_over_after_a_gap()
    {
        var book = new AchievementBook((string?)null);
        var day = new DateTime(2026, 11, 1, 12, 0, 0);
        for (int i = 0; i < 7; i++) { book.Used(day.AddDays(i)); book.Used(day.AddDays(i).AddHours(3)); } // twice a day counts once
        Assert.Equal(7, book.Value(AchievementBook.Streak));
        Assert.True(book.IsUnlocked(AchievementBook.All.Single(a => a.Id == "streak-7")));

        book.Used(day.AddDays(9)); // a day missed
        Assert.Equal(1, book.Value(AchievementBook.Streak));
        Assert.True(book.IsUnlocked(AchievementBook.All.Single(a => a.Id == "streak-7")));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void The_night_owl_is_awake_between_2_and_4(int hour, bool owl)
    {
        var book = new AchievementBook((string?)null);
        book.Used(new DateTime(2026, 11, 3, hour, 30, 0));
        Assert.Equal(owl, book.IsUnlocked(AchievementBook.All.Single(a => a.Id == "night-owl")));
    }

    [Fact]
    public void Progress_survives_a_restart_and_a_damaged_file_starts_over()
    {
        string path = Path.Combine(Path.GetTempPath(), "holm-ach-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var book = new AchievementBook(path);
            book.Add(AchievementBook.Messages, 3);
            book.Used(new DateTime(2026, 11, 3, 12, 0, 0));
            var again = new AchievementBook(path);
            Assert.Equal(3, again.Value(AchievementBook.Messages));
            Assert.True(again.IsUnlocked(AchievementBook.All.Single(a => a.Id == "chat-1")));
            again.Used(new DateTime(2026, 11, 4, 9, 0, 0));
            Assert.Equal(2, again.Value(AchievementBook.Streak)); // the day was kept

            File.WriteAllText(path, "{ broken");
            Assert.Equal(0, new AchievementBook(path).UnlockedCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Every_achievement_has_a_unique_id_and_a_counter_that_is_counted()
    {
        Assert.Equal(AchievementBook.All.Count, AchievementBook.All.Select(a => a.Id).Distinct().Count());
        Assert.All(AchievementBook.All, a => Assert.True(a.Goal > 0));
        var counters = typeof(AchievementBook).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetValue(null)!).ToHashSet();
        Assert.All(AchievementBook.All, a => Assert.Contains(a.Counter, counters));
    }

    [Fact]
    public void Friends_see_how_many_achievements_you_have()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Here, "Noah", 1, Trophies: 12)), out _);
        Assert.Equal(12, note!.Trophies);
        var odd = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Ping, "Noah", 1, Trophies: -4)), out _);
        Assert.Null(odd!.Trophies);
    }
}
