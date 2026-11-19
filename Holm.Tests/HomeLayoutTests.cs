using System.Text.Json;

namespace WinNotch.Tests;

public class HomeLayoutTests
{
    static AppSettings Read(string json) => JsonSerializer.Deserialize<AppSettings>(json)!;

    [Fact]
    public void Until_you_change_it_the_start_page_is_the_usual_one()
    {
        var layout = HomeLayout.Read(Read("{}"));

        Assert.True(layout.IsUsual);
        Assert.Equal(new[] { "tools", "main", "today", "live", "shelf", "status" }, layout.Sections);
        Assert.Equal(new[] { "notes", "reminders", "calculator", "chats", "clipboard", "mixer", "ai", "color" }, layout.ToolOrder);
        Assert.Empty(layout.Hidden);
        Assert.Empty(layout.Swapped);
        Assert.Empty(layout.HiddenTools);
    }

    [Fact]
    public void The_usual_layout_is_saved_as_nothing()
    {
        var settings = new AppSettings();
        HomeLayout.Usual.Write(settings);

        Assert.Empty(settings.HomeSections);
        Assert.Empty(settings.HomeHidden);
        Assert.Empty(settings.HomeSwapped);
        Assert.Empty(settings.HomeTools);
        Assert.Empty(settings.HomeHiddenTools);
    }

    [Fact]
    public void A_layout_survives_saving_and_reading()
    {
        var layout = HomeLayout.Usual
            .Move(HomeLayout.Status, 0)
            .Hide(HomeLayout.Today, true)
            .Swap(HomeLayout.Main, true)
            .MoveTool(HomeLayout.ColorPicker, 0)
            .HideTool(HomeLayout.ColorPicker, true);
        var settings = new AppSettings();
        layout.Write(settings);

        var again = HomeLayout.Read(Read(JsonSerializer.Serialize(settings)));

        Assert.False(again.IsUsual);
        Assert.Equal(new[] { "status", "tools", "main", "today", "live", "shelf" }, again.Sections);
        Assert.True(again.IsHidden(HomeLayout.Today));
        Assert.True(again.IsSwapped(HomeLayout.Main));
        Assert.Equal("color", again.ToolOrder[0]);
        Assert.Equal(8, again.ToolOrder.Count);
        Assert.True(again.IsToolHidden(HomeLayout.ColorPicker));
        Assert.Equal(again.ToolOrder, again.HideTool(HomeLayout.ColorPicker, false).ToolOrder);
    }

    [Fact]
    public void Every_tool_can_hide_and_reset_to_visible()
    {
        var layout = HomeLayout.Usual;
        foreach (string tool in HomeLayout.UsualTools) layout = layout.HideTool(tool, true);

        Assert.False(layout.IsUsual);
        Assert.Equal(HomeLayout.UsualTools, layout.HiddenTools);
        Assert.Equal(HomeLayout.UsualTools, layout.ToolOrder);
        var settings = new AppSettings();
        layout.Write(settings);
        HomeLayout.Usual.Write(settings);
        Assert.True(HomeLayout.Read(settings).IsUsual);
    }

    [Theory]
    [InlineData("main", 0, "main,tools,today,live,shelf,status")]
    [InlineData("tools", 5, "main,today,live,shelf,status,tools")]
    [InlineData("tools", 99, "main,today,live,shelf,status,tools")]
    [InlineData("status", -3, "status,tools,main,today,live,shelf")]
    [InlineData("today", 3, "tools,main,live,today,shelf,status")]
    [InlineData("live", 3, "tools,main,today,live,shelf,status")]
    [InlineData("nothing", 0, "tools,main,today,live,shelf,status")]
    public void Moving_a_section_puts_it_at_its_place_among_the_others(string section, int index, string expected)
        => Assert.Equal(expected.Split(','), HomeLayout.Usual.Move(section, index).Sections);

    [Fact]
    public void A_saved_order_is_made_whole()
    {
        // Unknown names go, a name twice counts once, and what's missing comes back after the one it usually follows.
        var settings = Read("""{"HomeSections":["status","bogus","main","status","tools"]}""");

        Assert.Equal(new[] { "status", "main", "today", "live", "shelf", "tools" }, HomeLayout.Read(settings).Sections);
    }

    [Fact]
    public void A_section_a_later_version_adds_shows_at_its_usual_place()
    {
        // Saved before "today" and "shelf" could move: they come back after "main" and "live".
        Assert.Equal(new[] { "status", "tools", "main", "today", "live", "shelf" },
            HomeLayout.Arrange(new[] { "status", "tools", "main", "live" }, HomeLayout.UsualSections));
        // A missing first section goes to the top.
        Assert.Equal(new[] { "tools", "status", "main", "today", "live", "shelf" },
            HomeLayout.Arrange(new[] { "status", "main" }, HomeLayout.UsualSections));
    }

    [Fact]
    public void Only_the_tool_bar_the_cards_and_the_battery_and_volume_can_hide()
    {
        var layout = HomeLayout.Usual;
        foreach (string section in HomeLayout.UsualSections) layout = layout.Hide(section, true);

        Assert.Equal(new[] { "tools", "today", "status" }, layout.Hidden);
        Assert.False(layout.IsHidden(HomeLayout.Main));
        Assert.False(layout.IsHidden(HomeLayout.Live));
        Assert.Empty(layout.Hide(HomeLayout.Tools, false).Hide(HomeLayout.Today, false).Hide(HomeLayout.Status, false).Hidden);
        // A hidden section the settings were edited to hold doesn't hide what always shows.
        Assert.Empty(HomeLayout.Read(Read("""{"HomeHidden":["main","shelf","live"]}""")).Hidden);
    }

    [Fact]
    public void Only_sections_with_two_sides_swap()
    {
        var layout = HomeLayout.Usual;
        foreach (string section in HomeLayout.UsualSections) layout = layout.Swap(section, true);

        Assert.Equal(new[] { "main", "today", "status" }, layout.Swapped);
        Assert.False(layout.Swap(HomeLayout.Main, false).IsSwapped(HomeLayout.Main));
        Assert.True(layout.Swap(HomeLayout.Main, true).IsSwapped(HomeLayout.Main));
    }

    [Fact]
    public void Moving_back_where_it_was_is_the_usual_layout_again()
    {
        var layout = HomeLayout.Usual.Move(HomeLayout.Status, 0).Move(HomeLayout.Status, 5).MoveTool(HomeLayout.Ai, 0).MoveTool(HomeLayout.Ai, 6);

        Assert.True(layout.IsUsual);
        var settings = new AppSettings { HomeSections = new() { "status" } };
        layout.Write(settings);
        Assert.Empty(settings.HomeSections);
        Assert.Empty(settings.HomeTools);
    }

    [Fact]
    public void The_start_page_goes_with_the_account()
    {
        foreach (string name in new[] { nameof(AppSettings.HomeSections), nameof(AppSettings.HomeHidden), nameof(AppSettings.HomeSwapped),
                                       nameof(AppSettings.HomeTools), nameof(AppSettings.HomeHiddenTools) })
            Assert.Contains(name, WinNotch.Account.SettingsSync.Names);
    }
}
