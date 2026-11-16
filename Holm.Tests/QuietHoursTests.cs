using System;
using System.Text.Json;

namespace WinNotch.Tests;

public class QuietHoursTests
{
    static DateTime At(int hour, int minute = 0) => new(2026, 10, 27, hour, minute, 0, DateTimeKind.Local);

    [Theory]
    [InlineData("22:00", 22, 0)]
    [InlineData("7:30", 7, 30)]
    [InlineData(" 00:00 ", 0, 0)]
    public void Parses_times(string text, int hour, int minute) => Assert.Equal(new TimeSpan(hour, minute, 0), QuietHours.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("22")]
    [InlineData("abc")]
    public void Rejects_what_is_no_time(string? text) => Assert.Null(QuietHours.Parse(text));

    [Fact]
    public void Formats_times() => Assert.Equal("07:00", QuietHours.Format(TimeSpan.FromHours(7)));

    [Theory]
    [InlineData(23, 0, true)]
    [InlineData(22, 0, true)]   // from is included
    [InlineData(3, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false)]   // until is not
    [InlineData(12, 0, false)]
    [InlineData(21, 59, false)]
    public void Overnight_hours(int hour, int minute, bool quiet)
        => Assert.Equal(quiet, QuietHours.IsQuiet(TimeSpan.FromHours(22), TimeSpan.FromHours(7), At(hour, minute)));

    [Theory]
    [InlineData(12, 30, true)]
    [InlineData(14, 0, false)]
    [InlineData(11, 59, false)]
    public void Hours_within_a_day(int hour, int minute, bool quiet)
        => Assert.Equal(quiet, QuietHours.IsQuiet(TimeSpan.FromHours(12), TimeSpan.FromHours(14), At(hour, minute)));

    [Fact]
    public void Equal_times_are_never_quiet() => Assert.False(QuietHours.IsQuiet(TimeSpan.FromHours(8), TimeSpan.FromHours(8), At(8)));

    static readonly string[] Vips = { "app:teams", "who:Lena", "who:Mum" };

    [Theory]
    [InlineData("teams", "Anyone", true)]
    [InlineData("TEAMS", "Anyone", true)]
    [InlineData("whatsapp", "Lena Hoffmann", true)]
    [InlineData("whatsapp", "Family: lena", true)]
    [InlineData("signal", "Mum", true)]
    [InlineData("whatsapp", "Magdalena", false)]   // whole words only
    [InlineData("whatsapp", "Mumford", false)]
    [InlineData("discord", "Jonas", false)]
    public void Vips_get_through(string app, string title, bool vip) => Assert.Equal(vip, QuietHours.IsVip(app, title, Vips));

    [Fact]
    public void A_name_with_regex_characters_is_taken_literally()
        => Assert.True(QuietHours.IsVip("x", "Team (Berlin)", new[] { "who:Team (Berlin)" }));

    [Fact]
    public void Blank_entries_match_nothing() => Assert.False(QuietHours.IsVip("teams", "Lena", new[] { "who: ", "app:", "Lena" }));

    [Fact]
    public void Silences_only_when_on_in_the_hours_and_not_a_vip()
    {
        Assert.True(QuietHours.Silences(true, "22:00", "07:00", At(23), "discord", "Jonas", Vips));
        Assert.False(QuietHours.Silences(false, "22:00", "07:00", At(23), "discord", "Jonas", Vips));
        Assert.False(QuietHours.Silences(true, "22:00", "07:00", At(12), "discord", "Jonas", Vips));
        Assert.False(QuietHours.Silences(true, "22:00", "07:00", At(23), "teams", "Jonas", Vips));
        Assert.False(QuietHours.Silences(true, "late", "07:00", At(23), "discord", "Jonas", Vips)); // a broken setting silences nothing
    }

    [Fact]
    public void Offers_who_wrote_lately_once_each()
    {
        var now = DateTimeOffset.Now;
        var people = QuietHours.RecentPeople(new[] { ("Lena", now.AddMinutes(-5)), ("Jonas", now), ("lena", now.AddMinutes(-1)), (" ", now), ("Tim", now.AddHours(-2)) }, 2);
        Assert.Equal(new[] { "Jonas", "lena" }, people);
    }

    [Fact]
    public void Settings_from_before_quiet_hours_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"TeamsMessages\":true,\"MutedApps\":[\"discord\"]}")!;
        Assert.False(old.QuietHours);
        Assert.Equal("22:00", old.QuietFrom);
        Assert.Equal("07:00", old.QuietUntil);
        Assert.Empty(old.QuietVips);
        Assert.Contains("discord", old.MutedApps);
    }
}
