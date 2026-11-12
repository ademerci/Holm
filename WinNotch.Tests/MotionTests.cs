using WinNotch;

namespace WinNotch.Tests;

public class MotionTests
{
    static IEnumerable<double> Steps() => Enumerable.Range(0, 201).Select(i => i / 200.0);

    [Fact]
    public void Both_springs_start_at_the_start_and_end_at_the_end()
    {
        foreach (var spring in new[] { Spring.Bouncy, Spring.Smooth })
        {
            Assert.Equal(0, spring.Ease(0));
            Assert.Equal(1, spring.Ease(1));
            Assert.InRange(spring.Ease(0.97), 0.995, 1.005); // at rest before the end: no jump on the last frame
        }
    }

    [Fact]
    public void Closing_glides_in_without_overshoot()
    {
        double last = 0;
        foreach (double t in Steps())
        {
            double x = Spring.Smooth.Ease(t);
            Assert.InRange(x, last, 1); // never back, never past the end (a shrinking height would go below zero)
            last = x;
        }
    }

    [Fact]
    public void Opening_swings_a_little_past_and_settles()
    {
        double peak = Steps().Max(Spring.Bouncy.Ease);
        Assert.InRange(peak, 1.005, 1.03);
        Assert.True(Spring.Bouncy.Ease(0.15) > 0.3, "it gets going right away");
    }

    [Theory]
    [InlineData(23, false, "night", true)]
    [InlineData(7, true, "dawn", false)]
    [InlineData(13, true, "day", false)]
    [InlineData(18, true, "dusk", false)]
    public void The_sky_follows_the_time_of_day(int hour, bool isDay, string name, bool stars)
    {
        var sky = Sky.For(new DateTime(2026, 11, 11, hour, 30, 0), WeatherKind.Clear, isDay);
        Assert.Equal(name, sky.Name);
        Assert.Equal(stars, sky.Stars);
    }

    [Fact]
    public void Without_weather_the_clock_decides_whether_it_is_day()
    {
        Assert.Equal("night", Sky.For(new DateTime(2026, 11, 11, 2, 0, 0), null, null).Name);
        Assert.Equal("day", Sky.For(new DateTime(2026, 11, 11, 12, 0, 0), null, null).Name);
    }

    [Fact]
    public void Weather_colours_the_sky()
    {
        var noon = new DateTime(2026, 11, 11, 12, 0, 0);
        Assert.Equal("rain", Sky.For(noon, WeatherKind.Rain, true).Name);
        Assert.Equal("rain at night", Sky.For(noon.AddHours(10), WeatherKind.Thunder, false).Name);
        Assert.Equal("snow", Sky.For(noon, WeatherKind.Snow, true).Name);
        Assert.Equal("fog", Sky.For(noon, WeatherKind.Fog, true).Name);
        Assert.Equal("clouds", Sky.For(noon, WeatherKind.Cloudy, true).Name);
        Assert.False(Sky.For(noon.AddHours(10), WeatherKind.Cloudy, false).Stars); // no stars behind clouds
    }

    [Theory]
    [InlineData(8, "Noah Example", "Good morning, Noah")]
    [InlineData(15, "Noah", "Good afternoon, Noah")]
    [InlineData(20, "", "Good evening")]
    [InlineData(2, "Noah", "Hello, night owl")]
    [InlineData(10, "  ", "Good morning")]
    [InlineData(10, "✨✨", "Good morning")]
    [InlineData(10, "Anne-Marie Muster", "Good morning, Anne-Marie")]
    public void The_greeting_fits_the_hour_and_uses_the_first_name(int hour, string name, string greeting)
        => Assert.Equal(greeting, Sky.Greeting(new DateTime(2026, 11, 11, hour, 0, 0), name));
}
