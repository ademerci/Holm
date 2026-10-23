namespace WinNotch.Tests;

public class ExitWatchdogTests
{
    [Fact]
    public void Ends_the_process_after_the_grace_period_and_says_why()
    {
        string? report = null;
        using var ended = new ManualResetEventSlim();
        var thread = ExitWatchdog.Arm(TimeSpan.FromMilliseconds(50), text => report = text, ended.Set);

        Assert.True(ended.Wait(TimeSpan.FromSeconds(5)), "The watchdog never fired.");
        Assert.Contains("still running", report);
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Waits_for_the_whole_grace_period()
    {
        using var ended = new ManualResetEventSlim();
        ExitWatchdog.Arm(TimeSpan.FromSeconds(2), _ => { }, ended.Set);
        Assert.False(ended.Wait(TimeSpan.FromMilliseconds(500)));
        Assert.True(ended.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Never_keeps_the_process_alive_itself()
    {
        var thread = ExitWatchdog.Arm(TimeSpan.FromMilliseconds(10), _ => { }, () => { });
        Assert.True(thread.IsBackground);
        Assert.Equal("Holm exit watchdog", thread.Name);
        thread.Join(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Waits_ten_seconds_in_the_app()
        => Assert.Equal(TimeSpan.FromSeconds(10), ExitWatchdog.Grace); // well inside the updater's 90 s wait
}
