namespace WinNotch.Tests;

public class CallsTests
{
    static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    static SensorUse Mic(string name, string key, int minutesAgo) => new(Sensor.Microphone, name, key, T0.AddMinutes(-minutesAgo));

    [Theory]
    [InlineData("Discord", true)]
    [InlineData("ms-teams", true)]
    [InlineData("Zoom", true)]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", true)]
    [InlineData("MSTeams_8wekyb3d8bbwe", true)]
    [InlineData("chrome", true)]
    [InlineData("obs64", false)]
    [InlineData("Resolve", false)]
    [InlineData("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", false)]
    public void Call_apps_are_known(string key, bool isCall) => Assert.Equal(isCall, Calls.IsCallApp(key));

    [Fact]
    public void No_call_while_only_other_apps_record()
        => Assert.Null(Calls.Current(new[] { Mic("OBS Studio", "obs64", 5), new SensorUse(Sensor.Camera, "Teams", "ms-teams", T0) }));

    [Fact]
    public void A_call_app_comes_before_a_browser()
    {
        var call = Calls.Current(new[] { Mic("Chrome", "chrome", 30), Mic("Discord", "Discord", 2) });
        Assert.Equal("Discord", call!.Key);
        Assert.Equal("Discord call", Calls.Title(call));
    }

    [Fact]
    public void The_longest_call_comes_first()
        => Assert.Equal("Zoom", Calls.Current(new[] { Mic("Discord", "Discord", 2), Mic("Zoom", "Zoom", 20) })!.Key);

    [Fact]
    public void A_call_in_the_browser_says_so()
        => Assert.Equal("Call in Edge", Calls.Title(Calls.Current(new[] { Mic("Edge", "msedge", 1) })!));
}
