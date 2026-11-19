using System;
using System.Linq;
using WinNotch;
using Xunit;

namespace Holm.Tests;

public class VerityTests
{
    static readonly DateTime Noon = new(2026, 11, 19, 12, 0, 0), Midnight = new(2026, 11, 19, 23, 30, 0);

    [Fact]
    public void She_is_awake_by_day_and_asleep_at_night()
    {
        Assert.Equal(VerityMood.Awake, Verity.Mood(Noon, music: false, pettedAt: null));
        Assert.Equal(VerityMood.Asleep, Verity.Mood(Midnight, music: false, pettedAt: null));
        Assert.Equal(VerityMood.Asleep, Verity.Mood(new DateTime(2026, 11, 19, 6, 59, 0), false, null));
        Assert.Equal(VerityMood.Awake, Verity.Mood(new DateTime(2026, 11, 19, 7, 0, 0), false, null));
    }

    [Fact]
    public void Music_wakes_her_to_dance_and_a_pet_beats_everything_for_a_moment()
    {
        Assert.Equal(VerityMood.Dancing, Verity.Mood(Midnight, music: true, pettedAt: null));
        Assert.Equal(VerityMood.Happy, Verity.Mood(Midnight, music: true, pettedAt: Midnight.AddSeconds(-1)));
        Assert.Equal(VerityMood.Asleep, Verity.Mood(Midnight, music: false, pettedAt: Midnight - Verity.HappyFor));
        Assert.Equal(VerityMood.Awake, Verity.Mood(Noon, music: false, pettedAt: Noon.AddSeconds(5))); // a clock set back
    }

    [Fact]
    public void Finding_her_unlocks_one_achievement_once()
    {
        var book = new AchievementBook(null);
        Assert.Equal(new[] { "verity" }, book.Set(AchievementBook.Verity, 1).Select(a => a.Id));
        Assert.Empty(book.Set(AchievementBook.Verity, 1));
        Assert.Contains(AchievementBook.All, a => a.Id == "verity" && a.Glyph == "");
    }

    [Fact]
    public void She_lives_in_the_notch_by_default_once_found() => Assert.True(new AppSettings().Verity);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Roaming_she_gets_around_but_never_leaves_her_floor(int seed)
    {
        var roam = new VerityRoam(480, 30, new Random(seed));
        double left = double.MaxValue, right = double.MinValue, highest = 0;
        for (int frame = 0; frame < 30 * 120; frame++) // two minutes at 30 frames a second
        {
            roam.Step(1 / 30.0, VerityMood.Awake);
            Assert.InRange(roam.X, 15, 465);
            Assert.InRange(roam.Lift, 0, 34);
            Assert.InRange(roam.Squash, -0.2, 0.25);
            left = Math.Min(left, roam.X);
            right = Math.Max(right, roam.X);
            highest = Math.Max(highest, roam.Lift);
        }
        Assert.True(right - left > 150, $"she only went from {left:0} to {right:0}");
        Assert.True(highest > 10);
    }

    [Fact]
    public void Asleep_she_stays_put_and_a_pet_makes_her_leap()
    {
        var roam = new VerityRoam(480, 30, new Random(1));
        double x = roam.X;
        for (int frame = 0; frame < 300; frame++) roam.Step(1 / 30.0, VerityMood.Asleep);
        Assert.Equal(x, roam.X);
        Assert.Equal(0, roam.Lift);

        roam.Jump();
        double highest = 0;
        for (int frame = 0; frame < 30; frame++) { roam.Step(1 / 30.0, VerityMood.Happy); highest = Math.Max(highest, roam.Lift); }
        Assert.True(highest > 25);
        Assert.Equal(x, roam.X);
    }

    [Fact]
    public void To_music_she_bounces_on_the_spot()
    {
        var roam = new VerityRoam(480, 30, new Random(1));
        double x = roam.X, highest = 0;
        for (int frame = 0; frame < 90; frame++) { roam.Step(1 / 30.0, VerityMood.Dancing); highest = Math.Max(highest, roam.Lift); }
        Assert.Equal(x, roam.X);
        Assert.True(highest > 5);
    }

    [Fact]
    public void A_narrower_notch_keeps_her_inside()
    {
        var roam = new VerityRoam(480, 30, new Random(1));
        roam.Resize(100);
        Assert.InRange(roam.X, 15, 85);
    }
}
