namespace WinNotch.Tests;

public class SleepTimerTests
{
    static readonly DateTime T0 = new(2026, 10, 27, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Goes_off_once_at_the_end_and_is_off_afterwards()
    {
        var sleep = new SleepTimer();
        sleep.Start(TimeSpan.FromMinutes(30), T0);

        Assert.True(sleep.Running);
        Assert.False(sleep.TakeDone(T0.AddMinutes(29.9)));
        Assert.True(sleep.TakeDone(T0.AddMinutes(30)));
        Assert.False(sleep.Running);
        Assert.False(sleep.TakeDone(T0.AddMinutes(31))); // the music is paused only once
    }

    [Fact]
    public void Cancelling_stops_it()
    {
        var sleep = new SleepTimer();
        sleep.Start(TimeSpan.FromMinutes(15), T0);
        sleep.Cancel();

        Assert.False(sleep.Running);
        Assert.False(sleep.TakeDone(T0.AddHours(1)));
        Assert.Equal(TimeSpan.Zero, sleep.Remaining(T0));
    }

    [Fact]
    public void Starting_again_replaces_the_old_time()
    {
        var sleep = new SleepTimer();
        sleep.Start(TimeSpan.FromMinutes(15), T0);
        sleep.Start(TimeSpan.FromMinutes(60), T0.AddMinutes(10));

        Assert.False(sleep.TakeDone(T0.AddMinutes(15)));
        Assert.Equal(TimeSpan.FromMinutes(60), sleep.Length);
        Assert.True(sleep.TakeDone(T0.AddMinutes(70)));
    }

    [Theory]
    [InlineData(1800, "30 min")]
    [InlineData(1741, "30 min")]
    [InlineData(1740, "29 min")]
    [InlineData(61, "2 min")]
    [InlineData(60, "1 min")]
    [InlineData(59, "< 1 min")]
    [InlineData(0, "0 min")]
    public void Shows_whole_minutes_rounded_up(int seconds, string expected)
        => Assert.Equal(expected, SleepTimer.Short(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1741, 1)]
    [InlineData(1790, 50)]
    [InlineData(1800, 60)]
    public void The_label_updates_exactly_when_the_minute_changes(int leftSeconds, int expectedSeconds)
        => Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), SleepTimer.UntilNextMinute(TimeSpan.FromSeconds(leftSeconds)));

    [Fact]
    public void Offers_15_30_and_60_minutes() => Assert.Equal(new[] { 15, 30, 60 }, SleepTimer.Choices);
}
