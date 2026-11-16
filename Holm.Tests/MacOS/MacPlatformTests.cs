using Holm.Platform;
using Holm.Platform.MacOS;

namespace Holm.Tests.MacOS;

/// <summary>The parts of the macOS backend that are plain logic: they run on every system, without a Mac.</summary>
public class MacPlatformTests
{
    const char Sep = (char)31;

    static string Fields(params string[] f) => string.Join(Sep, f) + "\n";

    [Fact]
    public void Spotify_answer_becomes_a_track_with_its_timeline_and_cover()
    {
        var parsed = MacMediaPlatform.Parse(
            Fields("playing", "Song", "Artist", "Album", "215.5", "42,25", "true", "true", "https://i.scdn.co/image/abc"),
            MacMediaPlatform.Spotify);

        Assert.NotNull(parsed);
        var (state, art) = parsed!.Value;
        Assert.True(state.IsPlaying);
        Assert.Equal("Song", state.Title);
        Assert.Equal("Artist", state.Artist);
        Assert.Equal("Album", state.Album);
        Assert.Equal("Spotify", state.App);
        Assert.Equal("com.spotify.client", state.AppId);
        Assert.True(state.Shuffle);
        Assert.Equal(2, state.Repeat); // Spotify's repeat is the whole list
        Assert.Equal(TimeSpan.FromSeconds(215.5), state.Timeline!.Value.Duration);
        Assert.Equal(TimeSpan.FromSeconds(42.25), state.Timeline!.Value.Position);
        Assert.Equal("https://i.scdn.co/image/abc", art);
    }

    [Fact]
    public void Music_answer_reads_its_repeat_modes_and_has_no_cover_address()
    {
        var parsed = MacMediaPlatform.Parse(Fields("paused", "Song", "Artist", "Album", "2.15E+2", "10", "false", "one"), MacMediaPlatform.Music);

        Assert.NotNull(parsed);
        var (state, art) = parsed!.Value;
        Assert.False(state.IsPlaying);
        Assert.False(state.Shuffle);
        Assert.Equal(1, state.Repeat);
        Assert.Equal(TimeSpan.FromSeconds(215), state.Timeline!.Value.Duration);
        Assert.Equal("", art);
        Assert.Equal(2, MacMediaPlatform.Parse(Fields("playing", "S", "", "", "1", "0", "false", "all"), MacMediaPlatform.Music)!.Value.State.Repeat);
        Assert.Equal(0, MacMediaPlatform.Parse(Fields("playing", "S", "", "", "1", "0", "false", "off"), MacMediaPlatform.Music)!.Value.State.Repeat);
    }

    [Theory]
    [InlineData("stopped\n")]
    [InlineData("")]
    [InlineData("playing\u001f\u001f\u001f\u001f1\u001f0\u001ffalse\u001foff")] // no title
    public void Nothing_playing_is_no_track(string output)
        => Assert.Null(MacMediaPlatform.Parse(output, MacMediaPlatform.Music));

    [Fact]
    public void A_track_without_a_length_has_no_timeline()
    {
        var parsed = MacMediaPlatform.Parse(Fields("playing", "Radio", "", "", "0", "12", "false", "off"), MacMediaPlatform.Music);
        Assert.Null(parsed!.Value.State.Timeline);
    }

    [Fact]
    public void Bluetooth_list_takes_devices_with_a_battery_connected_or_not()
    {
        const string json = """
            {"SPBluetoothDataType":[{
              "controller_properties":{"controller_address":"00:00:00:00:00:00"},
              "device_connected":[
                {"AirPods Pro":{"device_address":"AA:00","device_batteryLevelLeft":"80%","device_batteryLevelRight":"65%","device_batteryLevelCase":"20%","device_minorType":"Headphones"}},
                {"Magic Mouse":{"device_address":"AA:01","device_batteryLevelMain":"41%","device_minorType":"Mouse"}},
                {"Some Speaker":{"device_address":"AA:02","device_minorType":"Speaker"}}
              ],
              "device_not_connected":[
                {"Magic Keyboard":{"device_address":"AA:03","device_batteryLevelMain":"100%","device_minorType":"Keyboard"}}
              ]
            }]}
            """;

        var devices = MacBluetoothPlatform.Parse(json);

        Assert.Equal(new[] { "AirPods Pro", "Magic Keyboard", "Magic Mouse" }, devices.Select(d => d.Name));
        var airpods = devices[0];
        Assert.Equal(65, airpods.Battery); // the emptier bud; the case doesn't count
        Assert.Equal(DeviceGlyphs.Headphones, airpods.Glyph);
        Assert.True(airpods.IsConnected);
        Assert.False(devices[1].IsConnected);
        Assert.Equal(DeviceGlyphs.Keyboard, devices[1].Glyph);
        Assert.Equal(41, devices[2].Battery);
        Assert.Equal(DeviceGlyphs.Mouse, devices[2].Glyph);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"SPBluetoothDataType\":[{}]}")]
    public void Bluetooth_answers_without_devices_give_an_empty_list(string json)
        => Assert.Empty(MacBluetoothPlatform.Parse(json));

    [Fact]
    public void Battery_percent_is_the_average_of_internal_batteries_and_ignores_others()
    {
        var state = MacPowerPlatform.Combine(new MacPowerPlatform.PowerSource[]
        {
            new("InternalBattery", true, 4000, 5000, "AC Power", true),
            new("UPS", true, 10, 100, "Battery Power", false),
        });
        Assert.Equal(new PowerState(true, 80, true, true), state);

        Assert.False(MacPowerPlatform.Combine(new MacPowerPlatform.PowerSource[] { new("UPS", true, 10, 100, "AC Power", false) }).HasBattery);
        Assert.False(MacPowerPlatform.Combine(new MacPowerPlatform.PowerSource[] { new("InternalBattery", false, 50, 100, "AC Power", false) }).HasBattery);
        Assert.Equal(new PowerState(true, 37, false, false),
            MacPowerPlatform.Combine(new MacPowerPlatform.PowerSource[] { new("InternalBattery", true, 37, null, "Battery Power", false) }));
    }

    [Fact]
    public void Hit_test_turns_Cocoa_points_into_window_pixels_from_the_top()
    {
        // A 560 x 40 point window at the top of a 900 point high screen, on a Retina display.
        var frame = new NSRect { X = 100, Y = 860, Width = 560, Height = 40 };
        // Region: 200 px wide from 460 px, 60 px down from the top (100 x 30 points).
        Assert.True(MacNotchWindowPlatform.HitTest(new NSPoint { X = 100 + 250, Y = 900 - 1 }, frame, 2, 460, 0, 200, 60));
        Assert.True(MacNotchWindowPlatform.HitTest(new NSPoint { X = 100 + 230, Y = 900 - 29 }, frame, 2, 460, 0, 200, 60));
        Assert.False(MacNotchWindowPlatform.HitTest(new NSPoint { X = 100 + 229, Y = 900 - 10 }, frame, 2, 460, 0, 200, 60));
        Assert.False(MacNotchWindowPlatform.HitTest(new NSPoint { X = 100 + 250, Y = 900 - 31 }, frame, 2, 460, 0, 200, 60));
        Assert.False(MacNotchWindowPlatform.HitTest(new NSPoint { X = 100 + 250, Y = 900 - 1 }, frame, 2, 0, 0, 0, 0));
    }

    [Fact]
    public void Four_character_codes_are_big_endian()
        => Assert.Equal(0x644F7574u, MacAudioPlatform.Code("dOut"));
}

/// <summary>
/// The macOS backend against the real system: that every native call is declared right and none of them throws or
/// crashes. Only runs on a Mac (CI has one); elsewhere each test returns at once.
/// </summary>
public class MacNativeTests
{
    [Fact]
    public void Power_sources_can_be_read_and_watched()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var power = new MacPowerPlatform();
        power.Start();
        var state = power.Read();
        if (state.HasBattery) Assert.InRange(state.Percent, 0, 100);
        power.Stop();
    }

    [Fact]
    public void Volume_can_be_read_and_watched()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var audio = new MacAudioPlatform();
        audio.Start();
        // A build machine may have no output at all; then there is simply no volume.
        if (audio.TryGetVolume(out float level, out _)) Assert.InRange(level, 0f, 1f);
        Assert.False(audio.MeterWorking);
        audio.Stop();
    }

    [Fact]
    public void Bluetooth_scan_returns_without_hanging()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var devices = new MacBluetoothPlatform().ScanBatteries();
        Assert.All(devices, d => Assert.InRange(d.Battery, 0, 100));
    }

    [Fact]
    public async Task Media_read_finds_nothing_when_no_player_runs()
    {
        if (!OperatingSystem.IsMacOS()) return;
        // The build machine runs neither Spotify nor Music: nothing is addressed, nothing is started.
        var media = new MacMediaPlatform();
        Assert.Null(await media.ReadAsync());
        Assert.Null(await media.ReadCoverAsync());
    }
}
