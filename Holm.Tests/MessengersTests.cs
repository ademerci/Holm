using WinNotch.Notify;

namespace WinNotch.Tests;

public class MessengersTests
{
    [Theory]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", "whatsapp")]
    [InlineData("com.squirrel.Discord.Discord", "discord")]
    [InlineData("Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows", "outlook")]
    [InlineData("Microsoft.Office.OUTLOOK.EXE.15", "outlook")]
    [InlineData("microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail", "outlook")]
    [InlineData("TelegramMessengerLLP.TelegramDesktop_t4vj0pshhgkwm!Telegram.TelegramDesktop.Store", "telegram")]
    [InlineData("org.whispersystems.signal-desktop", "signal")]
    [InlineData("com.squirrel.slack.slack", "slack")]
    public void Knows_the_messengers_by_their_app_id(string appId, string key)
        => Assert.Equal(key, Messengers.ForApp(appId)?.Key);

    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude")]
    [InlineData("DolbyLaboratories.DolbySettings_rz1tebttyb220!App")]
    [InlineData("Windows.SystemToast.WiFiNetworkManager")]
    [InlineData("Windows.Defender.SecurityCenter")]
    [InlineData("Chrome")]
    [InlineData("MSEdge")]
    // Teams is left out on purpose: new, free and classic Teams.
    [InlineData("MSTeams_8wekyb3d8bbwe!MSTeams")]
    [InlineData("MicrosoftTeams_8wekyb3d8bbwe!MicrosoftTeams")]
    [InlineData("com.squirrel.Teams.Teams")]
    public void Ignores_other_apps(string appId) => Assert.Null(Messengers.ForApp(appId));

    [Fact]
    public void Falls_back_to_the_display_name_without_an_app_id()
    {
        Assert.Equal("whatsapp", Messengers.ForApp("", "WhatsApp")?.Key);
        Assert.Null(Messengers.ForApp("", "Microsoft Teams"));
        Assert.Null(Messengers.ForApp("", "Paint"));
    }

    [Theory]
    [InlineData("ms-teams", null)]
    [InlineData("olk", "outlook")]
    [InlineData("OUTLOOK", "outlook")]
    [InlineData("WhatsApp", "whatsapp")]
    [InlineData("Discord", "discord")]
    [InlineData("chrome", null)]
    [InlineData("folk", null)]
    [InlineData("", null)]
    public void Knows_which_app_is_in_front(string process, string? key)
        => Assert.Equal(key, Messengers.ForProcess(process)?.Key);

    [Fact]
    public void Reads_sender_and_first_line()
    {
        var time = DateTimeOffset.Now;
        var message = Messengers.Parse("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", null,
            new[] { "Lena Hoffmann", "  Can we\n  talk   before 3?  ", "" }, 42, time)!;
        Assert.Equal("whatsapp", message.App);
        Assert.Equal("Lena Hoffmann", message.Title);
        Assert.Equal("Can we talk before 3?", message.Text);
        Assert.Equal(42u, message.Id);
        Assert.Equal(time, message.Time);
        Assert.Null(message.Site);
    }

    [Fact]
    public void A_mail_shows_sender_and_subject()
    {
        var message = Messengers.Parse("Microsoft.Office.OUTLOOK.EXE.15", null, new[] { "Ana Weber", "Re: Lehrplan", "See you on Monday" }, 1, DateTimeOffset.Now)!;
        Assert.Equal(("outlook", "Ana Weber", "Re: Lehrplan"), (message.App, message.Title, message.Text));
    }

    [Fact]
    public void A_notification_without_text_shows_the_app_name()
    {
        var message = Messengers.Parse("com.squirrel.Discord.Discord", null, Array.Empty<string>(), 1, DateTimeOffset.Now)!;
        Assert.Equal(("Discord", ""), (message.Title, message.Text));
    }

    [Theory]
    [InlineData("web.whatsapp.com", "whatsapp")]
    [InlineData("discord.com", "discord")]
    [InlineData("outlook.office.com", "outlook")]
    public void Recognises_the_web_apps_in_a_browser(string site, string key)
    {
        var message = Messengers.Parse("Chrome", null, new[] { "Jonas", "Bist du da?", site }, 7, DateTimeOffset.Now)!;
        Assert.Equal(key, message.App);
        Assert.Equal(("Jonas", "Bist du da?"), (message.Title, message.Text));
        Assert.Equal(site, message.Site);
    }

    [Fact]
    public void Other_web_notifications_are_ignored()
    {
        Assert.Null(Messengers.Parse("Chrome", null, new[] { "Sale", "50% off today", "shop.example.com" }, 1, DateTimeOffset.Now));
        Assert.Null(Messengers.Parse("Chrome", null, new[] { "Jonas", "Bist du da?", "teams.microsoft.com" }, 1, DateTimeOffset.Now));
        Assert.Null(Messengers.Parse("MSTeams_8wekyb3d8bbwe!MSTeams", null, new[] { "Lena Hoffmann", "Hi" }, 1, DateTimeOffset.Now));
        // A message that only mentions a site somewhere doesn't make the browser a messenger.
        Assert.Null(Messengers.Parse("MSEdge", null,
            new[] { "News", "Read the whole story about the outage on discord.com and what the company said about it" }, 1, DateTimeOffset.Now));
    }
}

public class BadgeCounterTests
{
    [Fact]
    public void Existing_unread_messages_are_counted_without_a_new_arrival()
    {
        var counter = new BadgeCounter();
        Assert.False(counter.Update(3));
        Assert.Equal(3, counter.Count);
        Assert.Equal(3, counter.Unread);
    }

    [Fact]
    public void A_higher_number_means_new_messages()
    {
        var counter = new BadgeCounter();
        counter.Update(3);
        Assert.True(counter.Update(5));
        Assert.Equal(5, counter.Unread);
        Assert.False(counter.Update(5)); // same number again: nothing new
        Assert.Equal(5, counter.Unread);
    }

    [Fact]
    public void Reading_in_the_app_lowers_it()
    {
        var counter = new BadgeCounter();
        counter.Update(0);
        counter.Update(4);
        Assert.Equal(4, counter.Unread);
        Assert.False(counter.Update(1)); // read three of them in the app
        Assert.Equal(1, counter.Unread);
        Assert.False(counter.Update(0));
        Assert.Equal(0, counter.Unread);
        Assert.True(counter.Update(1)); // and the next one counts again
        Assert.Equal(1, counter.Unread);
    }

    [Fact]
    public void Looking_at_the_app_clears_it_until_the_next_one()
    {
        var counter = new BadgeCounter();
        counter.Update(0);
        counter.Update(2);
        counter.MarkSeen();
        Assert.Equal(0, counter.Unread);
        Assert.True(counter.Update(3));
        Assert.Equal(1, counter.Unread);
    }

    [Fact]
    public void A_lower_number_after_looking_doesnt_hide_the_next_message()
    {
        var counter = new BadgeCounter();
        counter.Update(0);
        counter.Update(3);
        counter.MarkSeen();      // seen at 3
        counter.Update(2);       // read one in the app
        Assert.True(counter.Update(3)); // a new one: 3 again, but it is new
        Assert.Equal(1, counter.Unread);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var counter = new BadgeCounter();
        counter.Update(1);
        counter.Update(4);
        counter.Reset();
        Assert.Null(counter.Count);
        Assert.Equal(0, counter.Unread);
        Assert.False(counter.Update(9));
        Assert.Equal(9, counter.Unread);
    }
}
