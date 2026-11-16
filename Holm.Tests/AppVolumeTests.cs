using System.Linq;
using System.Text.Json;

namespace WinNotch.Tests;

public class AppVolumeTests
{
    static AudioSessionInfo S(int pid, string exe, string name = "", int state = AudioSessionInfo.Active, float volume = 1, bool muted = false, bool system = false)
        => new(pid, exe, name, state, volume, muted, system);

    [Fact]
    public void One_row_per_app_with_all_its_sessions()
    {
        var rows = AppVolumeList.Build(new[]
        {
            S(10, @"C:\Chrome\chrome.exe", "", volume: 0.4f),
            S(11, @"C:\Chrome\chrome.exe", "", volume: 0.8f),
            S(20, @"C:\Spotify\Spotify.exe", "Spotify"),
        });
        Assert.Equal(2, rows.Count);
        var chrome = rows.Single(r => r.Name == "chrome");
        Assert.Equal(new[] { 0, 1 }, chrome.Sessions);
        Assert.Equal(0.8f, chrome.Volume);
    }

    [Fact]
    public void Playing_apps_come_first_then_by_name()
    {
        var rows = AppVolumeList.Build(new[]
        {
            S(1, @"C:\a\Zoom.exe", "Zoom", state: AudioSessionInfo.Inactive),
            S(2, @"C:\a\Spotify.exe", "Spotify"),
            S(3, @"C:\a\Discord.exe", "Discord", state: AudioSessionInfo.Inactive),
            S(4, @"C:\a\Arc.exe", "Arc"),
        });
        Assert.Equal(new[] { "Arc", "Spotify", "Discord", "Zoom" }, rows.Select(r => r.Name));
        Assert.True(rows[0].Playing);
        Assert.False(rows[3].Playing);
    }

    [Fact]
    public void Leaves_out_system_sounds_gone_sessions_and_itself()
    {
        var rows = AppVolumeList.Build(new[]
        {
            S(0, "", "System sounds", system: true),
            S(5, @"C:\a\Old.exe", "Old", state: AudioSessionInfo.Expired),
            S(42, @"C:\Holm\Holm.exe", "Holm"),
            S(6, @"C:\a\Game.exe", "Game"),
        }, ownProcessId: 42);
        Assert.Equal("Game", Assert.Single(rows).Name);
    }

    [Fact]
    public void Muted_only_when_every_session_is()
    {
        var some = AppVolumeList.Build(new[] { S(1, @"C:\b\x.exe", muted: true), S(2, @"C:\b\x.exe") });
        Assert.False(some[0].Muted);
        var all = AppVolumeList.Build(new[] { S(1, @"C:\b\x.exe", muted: true), S(2, @"C:\B\X.EXE", muted: true) });
        Assert.True(Assert.Single(all).Muted); // paths compare without case
    }

    [Fact]
    public void Names_from_the_session_or_the_program()
    {
        var rows = AppVolumeList.Build(new[] { S(1, @"C:\c\vlc.exe", "  "), S(2, @"C:\c\obs64.exe", "OBS Studio") });
        Assert.Contains(rows, r => r.Name == "vlc");
        Assert.Contains(rows, r => r.Name == "OBS Studio");
    }

    [Fact]
    public void Apps_without_a_known_file_stay_apart()
    {
        var rows = AppVolumeList.Build(new[] { S(7, "", "A"), S(8, "", "B") });
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void At_most_six_rows()
        => Assert.Equal(AppVolumeList.MaxRows, AppVolumeList.Build(Enumerable.Range(1, 10).Select(i => S(i, $@"C:\d\app{i}.exe")).ToList()).Count);

    [Theory]
    [InlineData(0.62f, false, "62 %")]
    [InlineData(1f, false, "100 %")]
    [InlineData(0.5f, true, "Muted")]
    public void Describes_the_level(float volume, bool muted, string expected) => Assert.Equal(expected, AppVolumeList.Describe(volume, muted));

    [Fact]
    public void Settings_from_before_the_mixer_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Weather\":false}")!;
        Assert.False(old.Weather);
        Assert.True(old.AppVolume);
    }
}
