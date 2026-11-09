using WinNotch;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class ActivityTests
{
    [Theory]
    [InlineData(FriendNoteKind.Ping)]
    [InlineData(FriendNoteKind.Here)]
    public void Asking_and_answering_carry_what_the_friend_is_doing(FriendNoteKind kind)
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var activity = new FriendActivity(FriendActivity.Music, "Blinding Lights", "The Weeknd", 1_700_000_000);

        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(kind, "Noah", 1, Activity: activity)), out _);

        Assert.Equal(kind, note!.Kind);
        Assert.Equal(activity, note.Activity);
    }

    [Fact]
    public void A_note_without_activity_is_a_friend_who_shares_nothing_or_runs_an_older_Holm()
    {
        using var me = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        var note = FriendNotes.Open(lena, FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Here, "Noah", 1)), out _);
        Assert.Equal(FriendNoteKind.Here, note!.Kind);
        Assert.Null(note.Activity);
    }

    [Fact]
    public void An_activity_of_an_unknown_kind_or_without_a_title_is_left_out()
    {
        Assert.Null(FriendActivity.From("cooking", "Pasta", null, 1));
        Assert.Null(FriendActivity.From(FriendActivity.Game, "  ", null, 1));
        Assert.Null(FriendActivity.From(null, "x", null, 1));
        var cut = FriendActivity.From(FriendActivity.Game, "Game\u0007 " + new string('x', 200), "\u0001", -5)!;
        Assert.True(cut.Title.Length <= 80);
        Assert.DoesNotContain(cut.Title, char.IsControl);
        Assert.Null(cut.Detail);
        Assert.Equal(0, cut.Since);
    }

    [Fact]
    public void The_longest_activity_still_fits_one_inbox_message()
    {
        using var me = FriendIdentity.Create(new string('N', 40), null);
        using var lena = FriendIdentity.Create("Lena", null);
        string emoji = string.Concat(Enumerable.Repeat("🎮", 40)); // 80 UTF-16 units, escaped by JSON
        var activity = new FriendActivity(FriendActivity.Game, emoji, emoji, long.MaxValue / 2);
        string text = FriendNotes.Seal(me, lena.AsFriend(), new FriendNote(FriendNoteKind.Here, me.Name, 1, Activity: activity));
        Assert.True(text.Length < 4096, $"{text.Length} characters");
    }

    [Theory]
    [InlineData(-30, "just now")]
    [InlineData(-12 * 60, "for 12 min")]
    [InlineData(-(2 * 3600 + 5 * 60), "for 2 h 5 min")]
    [InlineData(600, "")] // a clock ahead of ours
    public void The_time_reads_like_a_chat_app(int secondsAgo, string expected)
    {
        long since = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + secondsAgo;
        Assert.Equal(expected, ActivityPage.For(since));
    }
}
