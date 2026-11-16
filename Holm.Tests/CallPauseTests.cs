namespace WinNotch.Tests;

public class CallPauseTests
{
    static (bool Pause, bool Resume) Step(CallPause p, string? call, bool playing, string player = "Spotify")
    {
        p.Update(call, playing, player, out bool pause, out bool resume);
        return (pause, resume);
    }

    [Fact]
    public void Pauses_for_the_call_and_plays_again_afterwards()
    {
        var p = new CallPause();
        Assert.Equal((false, false), Step(p, null, true));
        Assert.Equal((true, false), Step(p, "Discord", true));
        Assert.True(p.Holding);
        Assert.Equal((false, false), Step(p, "Discord", false)); // the player confirms the pause
        Assert.Equal((false, true), Step(p, null, false));
        Assert.False(p.Holding);
    }

    [Fact]
    public void Music_that_was_not_playing_stays_off()
    {
        var p = new CallPause();
        Assert.Equal((false, false), Step(p, "Teams", false));
        Assert.Equal((false, false), Step(p, null, false));
    }

    [Fact]
    public void Resuming_during_the_call_means_hands_off()
    {
        var p = new CallPause();
        Step(p, "Zoom", true);
        Step(p, "Zoom", false);
        Step(p, "Zoom", true);  // you pressed play during the call
        Assert.False(p.Holding);
        Step(p, "Zoom", false); // and stopped it again
        Assert.Equal((false, false), Step(p, null, false));
    }

    [Fact]
    public void A_late_playing_report_right_after_pausing_is_not_you()
    {
        // Players often report "playing" once more before the pause arrives.
        var p = new CallPause();
        Step(p, "Discord", true);
        Step(p, "Discord", true);
        Assert.True(p.Holding);
        Step(p, "Discord", false);
        Assert.Equal((false, true), Step(p, null, false));
    }

    [Fact]
    public void Another_player_during_the_call_is_left_alone()
    {
        var p = new CallPause();
        Step(p, "Discord", true, "Spotify");
        Step(p, "Discord", false, "Spotify");
        Step(p, "Discord", true, "Chrome"); // you started a video
        Assert.Equal((false, false), Step(p, null, true, "Chrome"));
    }

    [Fact]
    public void Music_you_stopped_yourself_is_not_restarted_by_another_app()
    {
        var p = new CallPause();
        Step(p, "Discord", true, "Spotify");
        Assert.Equal((false, false), Step(p, null, false, "VLC"));
    }

    [Fact]
    public void Switching_call_apps_is_still_one_call()
    {
        var p = new CallPause();
        Step(p, "Teams", true);
        Step(p, "Teams", false);
        Assert.Equal((false, false), Step(p, "Zoom", false));
        Assert.Equal((false, true), Step(p, null, false));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var p = new CallPause();
        Step(p, "Teams", true);
        p.Reset();
        Assert.False(p.Holding);
        Assert.Equal((true, false), Step(p, "Teams", true)); // a new call starts fresh
    }
}
