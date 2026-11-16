using System.Text.Json;

namespace WinNotch.Tests;

public class ReminderParserTests
{
    // A Tuesday afternoon, local time.
    static readonly DateTime Now = new(2026, 10, 27, 13, 5, 0);

    [Theory]
    [InlineData("20 min Wäsche", 20, "Wäsche")]
    [InlineData("in 20 min laundry", 20, "Laundry")]
    [InlineData("20m tea", 20, "Tea")]
    [InlineData("1 h 30 min call Lena", 90, "Call Lena")]
    [InlineData("1h30m stretch", 90, "Stretch")]
    [InlineData("2 Stunden Parkuhr", 120, "Parkuhr")]
    [InlineData("1,5 Stunden lernen", 90, "Lernen")]
    [InlineData("Pizza in 12 Minuten", 12, "Pizza")]
    [InlineData("45 minutes", 45, "Reminder")]
    public void Relative_times(string input, int minutes, string text)
    {
        Assert.True(ReminderParser.TryParse(input, Now, out var due, out var what));
        Assert.Equal(Now.AddMinutes(minutes), due);
        Assert.Equal(text, what);
    }

    [Theory]
    [InlineData("14:30 Zahnarzt", 2026, 10, 27, 14, 30, "Zahnarzt")]
    [InlineData("dentist at 14.30", 2026, 10, 27, 14, 30, "Dentist")]
    [InlineData("um 17 Uhr Sport", 2026, 10, 27, 17, 0, "Sport")]
    [InlineData("9 Uhr Arzt", 2026, 10, 28, 9, 0, "Arzt")]           // 9:00 has passed today: tomorrow
    [InlineData("2:30pm pick up the kids", 2026, 10, 27, 14, 30, "Pick up the kids")]
    [InlineData("9am standup", 2026, 10, 28, 9, 0, "Standup")]
    [InlineData("12am backup", 2026, 10, 28, 0, 0, "Backup")]
    [InlineData("tomorrow 8:15 train", 2026, 10, 28, 8, 15, "Train")]
    [InlineData("morgen 7 Uhr Joggen", 2026, 10, 28, 7, 0, "Joggen")]
    [InlineData("übermorgen 10:00 Termin", 2026, 10, 29, 10, 0, "Termin")]
    [InlineData("13:04 just passed", 2026, 10, 28, 13, 4, "Just passed")]
    public void Times_of_day(string input, int y, int mo, int d, int h, int mi, string text)
    {
        Assert.True(ReminderParser.TryParse(input, Now, out var due, out var what));
        Assert.Equal(new DateTime(y, mo, d, h, mi, 0), due);
        Assert.Equal(text, what);
    }

    [Theory]
    [InlineData("")]
    [InlineData("laundry")]
    [InlineData("buy 3 apples")]          // a bare number is no time
    [InlineData("25:00 nope")]
    [InlineData("13pm nope")]
    [InlineData("0 min nothing")]
    [InlineData("30 days holiday")]
    [InlineData("200 h too far")]          // more than a week
    public void What_isnt_a_time_is_refused(string input)
        => Assert.False(ReminderParser.TryParse(input, Now, out _, out _));

    [Theory]
    [InlineData(20, "in 20 min")]
    [InlineData(59, "in 59 min")]
    [InlineData(85, "today at 14:30")]
    [InlineData(20 * 60, "tomorrow at 09:05")]
    [InlineData(3 * 24 * 60, "Fri at 13:05")]
    public void Says_when_in_short_words(int minutes, string expected)
        => Assert.Equal(expected, ReminderParser.Describe(Now.AddMinutes(minutes), Now));
}

public class ReminderListTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Keeps_them_in_order_and_takes_out_the_due_ones()
    {
        var list = new ReminderList(new List<Reminder>());
        list.Add("later", T0.AddHours(2));
        list.Add("soon", T0.AddMinutes(5));
        list.Add("middle", T0.AddHours(1));

        Assert.Equal(new[] { "soon", "middle", "later" }, list.Items.Select(r => r.Text));
        Assert.Empty(list.TakeDue(T0.AddMinutes(4)));
        Assert.Equal(new[] { "soon" }, list.TakeDue(T0.AddMinutes(5)).Select(r => r.Text));
        Assert.Equal("middle", list.Next!.Text);
    }

    [Fact]
    public void Reminders_missed_while_the_PC_was_off_come_all_at_once()
    {
        var list = new ReminderList(new List<Reminder>
        {
            new() { Text = "b", Due = T0.AddHours(-1) },
            new() { Text = "a", Due = T0.AddHours(-3) },
            new() { Text = "c", Due = T0.AddHours(1) },
        });
        Assert.Equal(new[] { "a", "b" }, list.TakeDue(T0).Select(r => r.Text));
        Assert.Single(list.Items);
    }

    [Fact]
    public void Waits_until_the_next_one_but_never_longer_than_a_quarter_of_an_hour()
    {
        var list = new ReminderList(new List<Reminder>());
        Assert.Null(list.Wait(T0));
        list.Add("x", T0.AddMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(3), list.Wait(T0));
        Assert.Equal(TimeSpan.Zero, list.Wait(T0.AddMinutes(10)));
        list.TakeDue(T0.AddMinutes(10));
        list.Add("y", T0.AddHours(5));
        Assert.Equal(TimeSpan.FromMinutes(15), list.Wait(T0));
    }

    [Fact]
    public void Removing_by_id()
    {
        var list = new ReminderList(new List<Reminder>());
        var r = list.Add("x", T0);
        Assert.True(list.Remove(r.Id));
        Assert.False(list.Remove(r.Id));
        Assert.Empty(list.Items);
    }

    [Fact]
    public void Reminders_survive_a_restart_through_the_settings_file()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(settings.Reminders);
        new ReminderList(settings.ReminderList).Add("Dentist", T0);
        var again = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        var restored = Assert.Single(again.ReminderList);
        Assert.Equal("Dentist", restored.Text);
        Assert.Equal(T0, restored.Due.ToUniversalTime());
    }
}
