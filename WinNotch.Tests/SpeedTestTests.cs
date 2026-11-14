namespace WinNotch.Tests;

public class SpeedTestTests
{
    [Theory]
    [InlineData(1_000_000, 1.0, 8.0)]          // 1 MB in a second is 8 Mbps
    [InlineData(125_000_000, 2.0, 500.0)]
    [InlineData(0, 1.0, 0.0)]
    [InlineData(1_000, 0.0, 0.0)]              // no time, no speed (not infinity)
    public void Mbps(long bytes, double seconds, double expected)
        => Assert.Equal(expected, SpeedTest.Mbps(bytes, TimeSpan.FromSeconds(seconds)), 6);

    [Fact]
    public void RateLeavesOutTheWarmup()
    {
        // 1 MB in the slow first 1.5 s, then 10 MB in the next 2 s: 40 Mbps, not 25
        double rate = SpeedTest.Rate(11_000_000, TimeSpan.FromSeconds(3.5), 1_000_000, TimeSpan.FromSeconds(1.5));
        Assert.Equal(40, rate, 6);
    }

    [Fact]
    public void RateUsesEverythingBeforeTheWarmupIsOver()
    {
        Assert.Equal(8, SpeedTest.Rate(1_000_000, TimeSpan.FromSeconds(1), -1, TimeSpan.Zero), 6);
        // less than a second after the warm-up is too short to judge on its own
        Assert.Equal(8, SpeedTest.Rate(2_000_000, TimeSpan.FromSeconds(2), 1_500_000, TimeSpan.FromSeconds(1.5)), 6);
    }

    [Fact]
    public void Median()
    {
        Assert.Equal(20, SpeedTest.Median(new[] { 30.0, 10, 20 }));
        Assert.Equal(15, SpeedTest.Median(new[] { 10.0, 20, 300, 12, 18, 11 }));
        Assert.Equal(0, SpeedTest.Median(Array.Empty<double>()));
    }

    [Theory]
    [InlineData(943.6, "944")]
    [InlineData(87.44, "87.4")]
    [InlineData(3.214, "3.21")]
    [InlineData(0, "0.00")]
    public void FormatsMbps(double mbps, string expected) => Assert.Equal(expected, SpeedTest.FormatMbps(mbps));

    [Theory]
    [InlineData(4.26, "4.3")]
    [InlineData(23.6, "24")]
    public void FormatsPing(double ms, string expected) => Assert.Equal(expected, SpeedTest.FormatPing(ms));

    [Fact]
    public void DescribesTheLink()
    {
        Assert.Equal("Wi-Fi · Home · 4 of 5 bars · link 866 Mbps",
            SpeedTest.DescribeLink(new NetworkLink(true, "Home", 4, 866_700_000)));
        Assert.Equal("Wi-Fi · 3 of 5 bars", SpeedTest.DescribeLink(new NetworkLink(true, null, 3, 0))); // name hidden by Windows
        Assert.Equal("Ethernet · link 1 Gbps", SpeedTest.DescribeLink(new NetworkLink(false, null, null, 1_000_000_000)));
        Assert.Equal("", SpeedTest.DescribeLink(null));
    }
}
