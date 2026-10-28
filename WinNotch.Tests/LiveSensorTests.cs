namespace WinNotch.Tests;

public class LiveSensorTests
{
    static readonly DateTime Now = new(2026, 10, 28, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Only_active_capture_sessions_count_even_when_muted()
    {
        var sessions = new[]
        {
            new AudioSessionInfo(1, @"C:\Discord\Discord.exe", "Discord", 1, 0, true, false),
            new AudioSessionInfo(2, @"C:\Discord\Discord.exe", "Discord", 1, 1, false, false),
            new AudioSessionInfo(3, @"C:\Teams\Teams.exe", "Teams", 0, 1, false, false),
            new AudioSessionInfo(4, @"C:\Zoom\Zoom.exe", "Zoom", 2, 1, false, false),
            new AudioSessionInfo(0, "", "System", 1, 1, false, true),
        };
        var use = Assert.Single(MicrophoneActivity.Active(sessions, Now));
        Assert.Equal("Discord", use.Key);
        Assert.Equal(Sensor.Microphone, use.Sensor);
        Assert.Equal(Now, use.Since);
        Assert.Equal("Discord", Calls.Current(new[] { use })!.Key);
    }

    [Fact]
    public void Live_sessions_find_current_discord_without_an_active_registry_entry()
    {
        var use = Assert.Single(MicrophoneActivity.Active(new[]
        {
            new AudioSessionInfo(12, @"C:\Discord\app-current\Discord.exe", "Discord", 1, 1, false, false)
        }, Now));
        Assert.Equal("Discord", Calls.Current(new[] { use })!.Key);
    }

    [Fact]
    public void Repeated_reports_preserve_start_time_but_stopped_and_restarted_capture_gets_a_new_one()
    {
        var first = new SensorUse(Sensor.Microphone, "Discord", "Discord", Now);
        var later = first with { Since = Now.AddMinutes(1) };
        Assert.Equal(Now, Assert.Single(PrivacyMonitor.StableLive(new[] { later }, new[] { first })).Since);
        Assert.Empty(PrivacyMonitor.StableLive(Array.Empty<SensorUse>(), new[] { first }));
        Assert.Equal(later.Since, Assert.Single(PrivacyMonitor.StableLive(new[] { later }, Array.Empty<SensorUse>())).Since);
    }
}
