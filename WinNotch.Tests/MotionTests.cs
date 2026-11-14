using WinNotch;

namespace WinNotch.Tests;

public class MotionTests
{
    static IEnumerable<double> Steps() => Enumerable.Range(0, 201).Select(i => i / 200.0);

    [Fact]
    public void Both_springs_start_at_the_start_and_end_at_the_end()
    {
        foreach (var spring in new[] { Spring.Bouncy, Spring.Snappy, Spring.Smooth })
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
    public void Opening_swings_past_and_settles()
    {
        double bouncyPeak = Steps().Max(Spring.Bouncy.Ease);
        double snappyPeak = Steps().Max(Spring.Snappy.Ease);
        Assert.InRange(bouncyPeak, 1.05, 1.09);
        Assert.InRange(snappyPeak, 1.08, 1.13);
        Assert.True(snappyPeak > bouncyPeak);
        Assert.True(Spring.Bouncy.Ease(0.15) > 0.4, "it gets going right away");
    }

    [Fact]
    public void Spring_value_overshoots_and_settles()
    {
        var spring = new SpringValue(30);
        spring.Retarget(240, 0.38, 0.60);
        double peak = 30;
        bool settled = false;
        for (int i = 0; i < 80 && !settled; i++)
        {
            settled = spring.Step(1.0 / 60);
            peak = Math.Max(peak, spring.Value);
        }
        Assert.True(settled);
        Assert.Equal(240, spring.Value);
        Assert.Equal(0, spring.Velocity);
        Assert.InRange(peak, 255, 265); // ~9.5 % overshoot past 240
    }

    [Fact]
    public void Critically_damped_spring_value_settles_without_overshoot()
    {
        var spring = new SpringValue(240);
        spring.Retarget(6, 0.27, 1.0);
        double last = 240;
        bool settled = false;
        for (int i = 0; i < 80 && !settled; i++)
        {
            settled = spring.Step(1.0 / 60);
            Assert.InRange(spring.Value, 6, last);
            last = spring.Value;
        }
        Assert.True(settled);
        Assert.Equal(6, spring.Value);
    }

    [Fact]
    public void Retargeting_mid_flight_preserves_momentum()
    {
        var spring = new SpringValue(30);
        spring.Retarget(240, 0.38, 0.60);
        for (int i = 0; i < 8; i++) spring.Step(1.0 / 60);
        double velocityBefore = spring.Velocity;
        Assert.True(velocityBefore > 400);

        spring.Retarget(150, 0.33, 0.75);
        Assert.Equal(velocityBefore, spring.Velocity);
        // Inertia carries it briefly upward before reversing toward 150.
        double afterOneFrame = spring.Value;
        spring.Step(1.0 / 120);
        Assert.True(spring.Value > afterOneFrame);
    }

    [Fact]
    public void Kick_adds_velocity_and_bounces_back()
    {
        var spring = new SpringValue(250);
        spring.Kick(40, 0.30, 0.58);
        Assert.False(spring.IsSettled);
        spring.Step(0.05);
        Assert.True(spring.Value > 250);
        bool settled = false;
        for (int i = 0; i < 80 && !settled; i++) settled = spring.Step(1.0 / 60);
        Assert.True(settled);
        Assert.Equal(250, spring.Value);
    }

    [Fact]
    public void Analytical_step_matches_substeps_regardless_of_frame_rate()
    {
        var coarse = new SpringValue(30);
        var fine = new SpringValue(30);
        coarse.Retarget(240, 0.38, 0.60);
        fine.Retarget(240, 0.38, 0.60);

        coarse.Step(0.2);
        for (int i = 0; i < 24; i++) fine.Step(0.2 / 24);

        Assert.InRange(coarse.Value, fine.Value - 1e-9, fine.Value + 1e-9);
        Assert.InRange(coarse.Velocity, fine.Velocity - 1e-9, fine.Velocity + 1e-9);
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
