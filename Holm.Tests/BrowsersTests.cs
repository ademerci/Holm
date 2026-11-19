namespace WinNotch.Tests;

public class BrowsersTests
{
    [Theory]
    [InlineData("chrome")]
    [InlineData("msedge")]
    [InlineData("Firefox")]
    [InlineData("brave")]
    public void Knows_the_usual_browsers(string process) => Assert.True(Browsers.Is(process));

    [Theory]
    [InlineData("")]
    [InlineData("explorer")]
    [InlineData("Spotify")]
    [InlineData("chromedriver")]
    public void Other_apps_are_not_browsers(string process) => Assert.False(Browsers.Is(process));

    [Fact]
    public void Hiding_over_browsers_is_off_unless_switched_on()
        => Assert.False(System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.HideOverBrowsers);
}
