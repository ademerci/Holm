using WinNotch;

namespace WinNotch.Tests;

/// <summary>Any app's notification as a pop-out in the notch (Settings › Pop-outs › All notifications in the notch).</summary>
public class NotificationPopTests
{
    static readonly DateTimeOffset At = new(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_line_is_the_title_and_the_rest_the_text()
    {
        var pop = MessagesService.Pop("Microsoft.Teams", "Teams", new[] { "Standup in 5 minutes", "Room 2", " " }, 7, At)!;
        Assert.Equal("Microsoft.Teams", pop.App);
        Assert.Equal("Standup in 5 minutes", pop.Title);
        Assert.Equal("Room 2", pop.Text);
        Assert.Equal(7u, pop.Id);
        Assert.Equal(At, pop.Time);
    }

    [Fact]
    public void Without_lines_the_apps_name_is_the_title_and_without_anything_there_is_no_pop_out()
    {
        Assert.Equal("Backup", MessagesService.Pop("Contoso.Backup", "Backup", Array.Empty<string>(), 1, At)!.Title);
        Assert.Null(MessagesService.Pop(null, null, new[] { "text" }, 1, At));
        Assert.Null(MessagesService.Pop("app", null, Array.Empty<string>(), 1, At));
    }

    [Fact]
    public void A_long_text_is_cut()
    {
        var pop = MessagesService.Pop("app", "App", new[] { "Title", new string('x', 500) }, 1, At)!;
        Assert.Equal(201, pop.Text.Length);
        Assert.EndsWith("…", pop.Text);
    }
}
