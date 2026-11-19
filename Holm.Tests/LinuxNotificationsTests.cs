using Holm.Platform.Linux;

namespace Holm.Tests;

/// <summary>Notify calls as dbus-monitor prints them (strings unescaped, over several lines when they have breaks).</summary>
public class LinuxNotificationsTests
{
    static readonly string[] Call =
    {
        "   string \"discord\"",
        "   uint32 0",
        "   string \"/tmp/discord.png\"",
        "   string \"Alice \"Al\"\"",
        "   string \"hey there",
        "second \"line\"\"",
        "   array [",
        "      string \"default\"",
        "      string \"Open\"",
        "   ]",
        "   array [",
        "      dict entry(",
        "         string \"urgency\"",
        "         variant             byte 1",
        "      )",
        "      dict entry(",
        "         string \"desktop-entry\"",
        "         variant             string \"discord\"",
        "      )",
        "   ]",
        "   int32 -1",
    };

    [Fact]
    public void A_notify_call_gives_its_app_text_and_desktop_entry()
    {
        var notify = LinuxNotificationsPlatform.ParseNotify(Call)!.Value;
        Assert.Equal("discord", notify.App);
        Assert.Equal("/tmp/discord.png", notify.Icon);
        Assert.Equal("Alice \"Al\"", notify.Summary);
        Assert.Equal("hey there\nsecond \"line\"", notify.Body);
        Assert.Equal("discord", notify.DesktopEntry);
    }

    [Fact]
    public void The_call_is_complete_at_its_timeout_not_before()
    {
        for (int i = 1; i < Call.Length; i++) Assert.False(LinuxNotificationsPlatform.IsLastArgument(Call[..i]), Call[i - 1]);
        Assert.True(LinuxNotificationsPlatform.IsLastArgument(Call));
    }
}
