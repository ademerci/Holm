using System;
using System.Text.Json;
using WinNotch.Sharing;

namespace WinNotch.Tests;

public class WaveTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Once_per_friend_every_thirty_seconds()
    {
        var limit = new WaveLimit();
        Assert.True(limit.Allow("lena", T0));
        Assert.False(limit.Allow("lena", T0.AddSeconds(10)));
        Assert.Equal(TimeSpan.FromSeconds(20), limit.Wait("lena", T0.AddSeconds(10)));
        Assert.True(limit.Allow("jonas", T0.AddSeconds(10))); // someone else may
        Assert.True(limit.Allow("lena", T0.AddSeconds(30)));
        Assert.Equal(TimeSpan.Zero, limit.Wait("tim", T0));
    }

    [Fact]
    public void At_most_so_many_an_hour()
    {
        var limit = new WaveLimit(perHour: 3);
        Assert.True(limit.Allow("a", T0));
        Assert.True(limit.Allow("b", T0.AddMinutes(1)));
        Assert.True(limit.Allow("c", T0.AddMinutes(2)));
        Assert.False(limit.Allow("d", T0.AddMinutes(3)));
        Assert.True(limit.Allow("d", T0.AddMinutes(60)));  // the first one is an hour old
    }

    [Fact]
    public void A_clock_set_back_does_not_block_forever()
    {
        var limit = new WaveLimit();
        Assert.True(limit.Allow("lena", T0));
        Assert.True(limit.Allow("lena", T0.AddMinutes(-10)));
    }

    [Fact]
    public void A_wave_travels_sealed_and_opens_as_a_wave()
    {
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.Seal(noah, lena.AsFriend(), new FriendNote(FriendNoteKind.Wave, "Noah", 1_800_000_000));
        var note = FriendNotes.Open(lena, text, out string sender);
        Assert.Equal(FriendNoteKind.Wave, note!.Kind);
        Assert.Equal("Noah", note.From);
        Assert.Equal(noah.PublicKey, sender);
        Assert.True(text.Length < 1000);
    }

    [Fact]
    public void Kinds_from_a_newer_holm_are_left_unopened_without_failing()
    {
        // What Holm before 1.7 does with "wave", this version does with whatever comes after it.
        using var noah = FriendIdentity.Create("Noah", null);
        using var lena = FriendIdentity.Create("Lena", null);
        string text = FriendNotes.SealKind(noah, lena.AsFriend(), "hologram", "Noah", 1_800_000_000);
        Assert.Null(FriendNotes.Open(lena, text, out string sender));
        Assert.Equal("", sender);
    }

    [Fact]
    public void Settings_from_before_waves_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"AppVolume\":false}")!;
        Assert.False(old.AppVolume);
        Assert.True(old.Waves);
    }
}
