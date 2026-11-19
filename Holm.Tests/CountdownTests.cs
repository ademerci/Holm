namespace WinNotch.Tests;

public class CountdownTests
{
    static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Runs_down_and_goes_off_at_the_end()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(5), T0);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining(T0));
        Assert.Equal(TimeSpan.FromSeconds(90), timer.Remaining(T0.AddSeconds(210)));
        Assert.False(timer.IsDone(T0.AddSeconds(299.9)));
        Assert.True(timer.IsDone(T0.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, timer.Remaining(T0.AddMinutes(7)));
    }

    [Fact]
    public void Late_ticks_never_make_it_slow()
    {
        // Whatever happens in between (sleep, a busy PC), it ends when it should.
        var timer = new Countdown(TimeSpan.FromMinutes(25), T0);
        Assert.True(timer.IsDone(T0.AddMinutes(25)));
    }

    [Fact]
    public void Pausing_stops_the_clock()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(10), T0);
        timer.Pause(T0.AddMinutes(4));
        Assert.True(timer.Paused);
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining(T0.AddHours(3)));
        Assert.False(timer.IsDone(T0.AddHours(3)));

        timer.Resume(T0.AddHours(3));
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining(T0.AddHours(3)));
        Assert.True(timer.IsDone(T0.AddHours(3).AddMinutes(6)));
    }

    [Fact]
    public void Pausing_or_resuming_twice_changes_nothing()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(10), T0);
        timer.Pause(T0.AddMinutes(1));
        timer.Pause(T0.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(9), timer.Remaining(T0.AddMinutes(5)));
        timer.Resume(T0.AddMinutes(5));
        timer.Resume(T0.AddMinutes(8));
        Assert.Equal(TimeSpan.FromMinutes(6), timer.Remaining(T0.AddMinutes(8)));
    }

    [Fact]
    public void More_time_adds_to_what_is_left()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(5), T0);
        timer.Add(TimeSpan.FromMinutes(5), T0.AddMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(8), timer.Remaining(T0.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromMinutes(10), timer.Length);

        timer.Pause(T0.AddMinutes(3));
        timer.Add(TimeSpan.FromMinutes(1), T0.AddMinutes(4));
        Assert.Equal(TimeSpan.FromMinutes(8), timer.Remaining(T0.AddMinutes(9)));
    }

    [Fact]
    public void More_time_after_it_went_off_counts_from_now()
    {
        var timer = new Countdown(TimeSpan.FromMinutes(1), T0);
        timer.Add(TimeSpan.FromMinutes(5), T0.AddMinutes(3));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.Remaining(T0.AddMinutes(3)));
    }

    [Fact]
    public void A_timer_needs_a_length()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Countdown(TimeSpan.Zero, T0));

    [Theory]
    [InlineData(300, "5:00")]
    [InlineData(299.2, "5:00")] // rounded up: 0:00 only at the very end
    [InlineData(299, "4:59")]
    [InlineData(59.5, "1:00")]
    [InlineData(0.4, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-3, "0:00")]
    [InlineData(1500, "25:00")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3905, "1:05:05")]
    public void Time_left_reads_like_a_clock(double seconds, string expected)
        => Assert.Equal(expected, Countdown.Format(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(25, "25 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 h 30 min")]
    [InlineData(120, "2 hours")]
    public void Lengths_are_described_in_words(int minutes, string expected)
        => Assert.Equal(expected, Countdown.Describe(TimeSpan.FromMinutes(minutes)));
}
