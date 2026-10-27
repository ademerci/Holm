using System.Text.Json;

namespace WinNotch.Tests;

public class NotepadTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("\n  \r\n", "")]
    [InlineData("Buy oat milk", "Buy oat milk")]
    [InlineData("\n\n  Call Lena  \nBuy milk", "Call Lena")]
    [InlineData("Line one\r\nLine two", "Line one")]
    public void The_preview_is_the_first_line_with_something_on_it(string note, string expected)
        => Assert.Equal(expected, Notepad.Preview(note));

    [Fact]
    public void A_long_first_line_is_shortened()
    {
        string preview = Notepad.Preview(new string('x', 200), max: 20);
        Assert.Equal(new string('x', 20) + "…", preview);
    }

    [Fact]
    public void Notes_are_limited_in_length()
    {
        Assert.Equal(Notepad.MaxLength, Notepad.Clamp(new string('a', Notepad.MaxLength + 50)).Length);
        Assert.Equal("short", Notepad.Clamp("short"));
    }

    [Fact]
    public void The_note_survives_a_restart_through_the_settings_file()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(settings.Notes);
        Assert.Equal("", settings.NoteText);

        settings.NoteText = "Book the train\nfor Friday";
        var again = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal("Book the train\nfor Friday", again.NoteText);
    }

    [Fact]
    public void Settings_from_before_notes_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Animations\":false,\"TeamsMessages\":true,\"Scale\":1.15}")!;
        Assert.False(old.Animations);
        Assert.True(old.Notes);
        Assert.True(old.SleepTimer);
        Assert.True(old.PauseForCalls);
        Assert.Equal("", old.NoteText);
    }
}
