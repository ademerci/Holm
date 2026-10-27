using System;

namespace WinNotch.Tests;

public class TodayTests
{
    static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_reminder_today_comes_first()
    {
        Assert.Equal(IdleStatus.Reminder, Today.Pick(Now.AddHours(5), Now, shelfCount: 4));
        Assert.Equal(IdleStatus.Reminder, Today.Pick(Now.AddHours(12), Now, 0));
    }

    [Fact]
    public void Then_the_shelf_then_nothing()
    {
        Assert.Equal(IdleStatus.Shelf, Today.Pick(Now.AddDays(2), Now, 3));
        Assert.Equal(IdleStatus.Shelf, Today.Pick(null, Now, 1));
        Assert.Equal(IdleStatus.None, Today.Pick(Now.AddHours(13), Now, 0));
        Assert.Equal(IdleStatus.None, Today.Pick(null, Now, 0));
    }

    [Theory]
    [InlineData(null, "Notes", "Jot something down")]
    [InlineData("  \n \n", "Notes", "Jot something down")]
    [InlineData("Buy oat milk", "Buy oat milk", "Your note")]
    [InlineData("Call Lena\n\nBuy oat milk", "Call Lena", "Buy oat milk")]
    [InlineData("Call Lena\r\nBuy oat milk\r\nBook the train\r\nPack", "Call Lena", "Buy oat milk · +2 more")]
    public void The_note_card_shows_its_first_lines(string? note, string title, string more)
        => Assert.Equal((title, more), Today.NoteCard(note));

    [Fact]
    public void The_reminder_card()
    {
        Assert.Equal(("No reminders", "Add one: 20 min laundry"), Today.ReminderCard(null, Now.ToLocalTime()));
        var dentist = new Reminder { Text = "Dentist", Due = DateTime.UtcNow.AddMinutes(30) };
        var (title, more) = Today.ReminderCard(dentist, DateTime.Now);
        Assert.Equal("Dentist", title);
        Assert.Equal(ReminderParser.Describe(dentist.Due.ToLocalTime(), DateTime.Now), more);
    }
}
