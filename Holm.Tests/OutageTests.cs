using System;
using System.Text.Json;

namespace WinNotch.Tests;

public class OutageTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0x0, false)]
    [InlineData(0x1 | 0x2, false)]      // no traffic
    [InlineData(0x20, false)]           // local network only (a captive portal, a router without uplink)
    [InlineData(0x40, true)]            // IPv4 internet
    [InlineData(0x400, true)]           // IPv6 internet
    [InlineData(0x40 | 0x200, true)]
    public void Internet_means_the_internet_flags(int connectivity, bool online) => Assert.Equal(online, Outage.HasInternet(connectivity));

    [Fact]
    public void Tells_an_outage_once_after_the_grace_and_how_long_it_took()
    {
        var outage = new Outage();
        outage.Lost(T0);
        Assert.False(outage.StillLost(T0.AddSeconds(1)));
        Assert.True(outage.StillLost(T0.AddSeconds(2)));
        Assert.False(outage.StillLost(T0.AddSeconds(3)));    // once
        outage.Lost(T0.AddSeconds(30));                       // another drop report while out: same outage
        Assert.Equal(TimeSpan.FromMinutes(3), outage.Back(T0.AddMinutes(3)));
        Assert.Null(outage.Back(T0.AddMinutes(4)));          // nothing left
    }

    [Fact]
    public void A_short_drop_is_no_outage()
    {
        var outage = new Outage();
        outage.Lost(T0);
        Assert.Null(outage.Back(T0.AddSeconds(1)));          // back before the grace: never told, nothing to say
        Assert.False(outage.StillLost(T0.AddSeconds(5)));
    }

    [Fact]
    public void Sleep_is_hushed()
    {
        var outage = new Outage();
        outage.Hush(T0.AddSeconds(30));
        outage.Lost(T0.AddSeconds(5));
        Assert.False(outage.StillLost(T0.AddSeconds(10)));
        Assert.Null(outage.Back(T0.AddSeconds(20)));
        outage.Lost(T0.AddSeconds(40));                       // after the hush it counts again
        Assert.True(outage.StillLost(T0.AddSeconds(43)));
    }

    [Fact]
    public void Going_to_sleep_mid_outage_forgets_it()
    {
        var outage = new Outage();
        outage.Lost(T0);
        Assert.True(outage.StillLost(T0.AddSeconds(3)));
        outage.Hush(T0.AddMinutes(10));
        Assert.Null(outage.Back(T0.AddMinutes(11)));
    }

    [Theory]
    [InlineData(0.4, "1 s")]
    [InlineData(40, "40 s")]
    [InlineData(185, "3 min")]
    [InlineData(3600, "1 h")]
    [InlineData(3900, "1 h 5 min")]
    public void Describes_how_long(double seconds, string expected) => Assert.Equal(expected, Outage.Describe(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Settings_from_before_the_connection_note_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"UsbDrives\":false}")!;
        Assert.False(old.UsbDrives);
        Assert.True(old.Connectivity);
    }
}
