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
    public void She_waits_until_switched_on_even_with_the_old_setting_saved()
    {
        Assert.False(new AppSettings().ShowVerity);
        Assert.False(new AppSettings().VerityInClosedNotch); // the open notch only, unless switched on
        // Every settings file before this saved the old "Verity" switch as on: it must not bring her in.
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"Verity":true}""")!.ShowVerity);
    }

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
    public void Without_a_treat_for_hours_she_is_hungry_but_sleep_and_music_come_first()
    {
        var fed = Noon - Verity.HungryAfter;
        Assert.Equal(VerityMood.Hungry, Verity.Mood(Noon, false, null, fed));
        Assert.Equal(VerityMood.Awake, Verity.Mood(Noon, false, null, fed.AddMinutes(1)));
        Assert.Equal(VerityMood.Awake, Verity.Mood(Noon, false, null, null));
        Assert.Equal(VerityMood.Dancing, Verity.Mood(Noon, true, null, fed));
        Assert.Equal(VerityMood.Asleep, Verity.Mood(Midnight, false, null, fed));
        Assert.True(Verity.Sleepy(new DateTime(2026, 11, 19, 21, 30, 0)));
        Assert.False(Verity.Sleepy(Noon));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Beside_buttons_she_keeps_to_her_free_stretch(int seed)
    {
        // A row with buttons from 100 to 380: free from 0 to 100 and from 380 to 480.
        var roam = new VerityRoam(480, 30, new Random(seed), headroom: 16);
        var free = new[] { (0.0, 100.0), (380.0, 480.0) };
        roam.SetFloor(480, free);
        roam.Notice(250); // the mouse over the buttons: she comes as close as her stretch goes
        for (int frame = 0; frame < 30 * 60; frame++)
        {
            roam.Step(1 / 30.0, VerityMood.Awake);
            if (frame > 60) Assert.True(roam.X <= 85 || roam.X >= 395, $"she stood on a button at {roam.X:0}");
            Assert.InRange(roam.Lift, 0, 16);
        }
    }

    [Fact]
    public void Put_down_on_the_buttons_she_hops_off_them()
    {
        var roam = new VerityRoam(480, 30, new Random(1), headroom: 16);
        roam.SetFloor(480, new[] { (0.0, 100.0), (380.0, 480.0) });
        roam.Grab();
        roam.HoldAt(200, 10);
        roam.Throw(0, 0);
        for (int frame = 0; frame < 30 * 5; frame++) roam.Step(1 / 30.0, VerityMood.Asleep); // even asleep
        Assert.InRange(roam.X, 15, 85);
        Assert.Equal(0, roam.Lift);
        Assert.Equal(0, roam.Angle);
    }

    [Fact]
    public void Thrown_she_flies_bounces_stays_on_her_floor_and_lands_upright()
    {
        var roam = new VerityRoam(480, 30, new Random(1), headroom: 16);
        roam.Grab();
        roam.HoldAt(240, 16);
        roam.Throw(900, 300);
        bool flew = false;
        for (int frame = 0; frame < 30 * 4; frame++)
        {
            roam.Step(1 / 30.0, VerityMood.Awake);
            Assert.InRange(roam.X, 15, 465);
            Assert.InRange(roam.Lift, 0, 16);
            flew |= roam.Angle != 0;
        }
        Assert.True(flew);
        for (int frame = 0; frame < 60; frame++) roam.Step(1 / 30.0, VerityMood.Asleep);
        Assert.False(roam.Moving);
        Assert.Equal(0, roam.Angle);
    }

    [Fact]
    public void A_treat_falls_she_hops_over_and_eats_it()
    {
        var roam = new VerityRoam(480, 30, new Random(1));
        int ate = 0;
        roam.Ate += () => ate++;
        roam.Feed(roam.X - 60);
        double at = roam.Treat!.Value.X;
        bool chewed = false;
        for (int frame = 0; frame < 30 * 8 && ate == 0; frame++)
        {
            roam.Step(1 / 30.0, VerityMood.Dancing); // a treat beats dancing
            chewed |= roam.Eating;
        }
        Assert.Equal(1, ate);
        Assert.True(chewed);
        Assert.Null(roam.Treat);
        Assert.InRange(roam.X, at - 2, at + 2);
    }

    [Fact]
    public void A_narrower_notch_keeps_her_inside()
    {
        var roam = new VerityRoam(480, 30, new Random(1));
        roam.Resize(100);
        Assert.InRange(roam.X, 15, 85);
    }
}
