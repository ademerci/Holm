using Holm.Platform.Linux;
using Tmds.DBus.Protocol;

namespace Holm.Tests.Linux;

/// <summary>The parts of the Linux backend that are plain logic: they run on every system, without a Linux desktop.</summary>
public class LinuxPlatformTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "holm-linux-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    void Supply(string name, params (string File, string Value)[] files)
    {
        string folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(folder);
        foreach (var (file, value) in files) File.WriteAllText(Path.Combine(folder, file), value + "\n");
    }

    LinuxPowerPlatform Power() => new(_folder, Bus.System);

    [Fact]
    public void No_power_supplies_means_no_battery()
    {
        Assert.False(Power().Read().HasBattery);
        Directory.CreateDirectory(_folder);
        Assert.False(Power().Read().HasBattery);
    }

    [Fact]
    public void A_desktop_with_only_mains_has_no_battery()
    {
        Supply("AC", ("type", "Mains"), ("online", "1"));
        Assert.False(Power().Read().HasBattery);
    }

    [Fact]
    public void A_laptop_on_battery()
    {
        Supply("AC", ("type", "Mains"), ("online", "0"));
        Supply("BAT0", ("type", "Battery"), ("capacity", "73"), ("status", "Discharging"));
        var state = Power().Read();
        Assert.True(state.HasBattery);
        Assert.Equal(73, state.Percent);
        Assert.False(state.OnAc);
        Assert.False(state.Charging);
    }

    [Fact]
    public void A_laptop_charging()
    {
        Supply("ADP1", ("type", "Mains"), ("online", "1"));
        Supply("BAT0", ("type", "Battery"), ("capacity", "40"), ("status", "Charging"));
        var state = Power().Read();
        Assert.True(state.OnAc);
        Assert.True(state.Charging);
    }

    [Fact]
    public void Plugged_in_and_full_is_on_mains_but_not_charging()
    {
        Supply("AC", ("type", "Mains"), ("online", "1"));
        Supply("BAT0", ("type", "Battery"), ("capacity", "100"), ("status", "Full"));
        var state = Power().Read();
        Assert.True(state.OnAc);
        Assert.False(state.Charging);
    }

    [Fact]
    public void Without_a_mains_entry_the_battery_says_whether_it_is_plugged_in()
    {
        Supply("BAT0", ("type", "Battery"), ("capacity", "55"), ("status", "Not charging"));
        Assert.True(Power().Read().OnAc);
    }

    [Fact]
    public void A_mouse_battery_is_not_the_computers()
    {
        Supply("hidpp_battery_0", ("type", "Battery"), ("scope", "Device"), ("capacity", "12"), ("status", "Discharging"));
        Assert.False(Power().Read().HasBattery);

        Supply("BAT0", ("type", "Battery"), ("scope", "System"), ("capacity", "80"), ("status", "Discharging"));
        Assert.Equal(80, Power().Read().Percent);
    }

    [Fact]
    public void Two_batteries_show_their_average()
    {
        Supply("BAT0", ("type", "Battery"), ("capacity", "100"), ("status", "Full"));
        Supply("BAT1", ("type", "Battery"), ("capacity", "41"), ("status", "Discharging"));
        Assert.Equal(71, Power().Read().Percent); // 70.5
    }

    [Theory]
    [InlineData("audio-headset", "")]
    [InlineData("audio-headphones", "")]
    [InlineData("input-mouse", "")]
    [InlineData("input-keyboard", "")]
    [InlineData("input-gaming", "")]
    [InlineData("phone", "")]
    [InlineData("", "")]
    [InlineData("something-new", "")]
    public void A_bluetooth_device_gets_the_icon_of_its_kind(string icon, string glyph)
        => Assert.Equal(glyph, LinuxBluetoothPlatform.GlyphFor(icon));

    [Theory]
    [InlineData("org.mpris.MediaPlayer2.spotify", "Spotify")]
    [InlineData("org.mpris.MediaPlayer2.firefox.instance_1_23", "Firefox")]
    [InlineData("org.mpris.MediaPlayer2.", "")]
    public void A_player_without_a_name_is_called_after_its_bus_name(string busName, string expected)
        => Assert.Equal(expected, LinuxMediaPlatform.FallbackName(busName));

    [Fact]
    public void Lengths_are_read_whatever_number_type_the_player_sends()
    {
        Assert.Equal(215_000_000, LinuxMediaPlatform.Number(VariantValue.Int64(215_000_000)));
        Assert.Equal(215_000_000, LinuxMediaPlatform.Number(VariantValue.UInt64(215_000_000)));
        Assert.Equal(215_000_000, LinuxMediaPlatform.Number(VariantValue.Double(215_000_000)));
        Assert.Equal(70, LinuxMediaPlatform.Number(VariantValue.Byte(70)));
        Assert.Null(LinuxMediaPlatform.Number(VariantValue.String("215")));
    }

    [Fact]
    public void Artists_are_joined_and_a_plain_string_is_taken_as_it_is()
    {
        Assert.Equal("A, B", LinuxMediaPlatform.JoinStrings(VariantValue.Array(new[] { "A", "", "B" })));
        Assert.Equal("Solo", LinuxMediaPlatform.JoinStrings(VariantValue.String("Solo")));
    }

    [Fact]
    public async Task A_cover_is_read_from_a_file_or_a_data_url()
    {
        Directory.CreateDirectory(_folder);
        string file = Path.Combine(_folder, "cover with space.jpg");
        File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
        Assert.Equal(new byte[] { 1, 2, 3 }, await LinuxMediaPlatform.LoadCoverAsync(new Uri(file).AbsoluteUri));
        Assert.Equal(new byte[] { 1, 2, 3 }, await LinuxMediaPlatform.LoadCoverAsync("data:image/png;base64,AQID"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("data:image/png,plain")]
    [InlineData("file:///nowhere/at/all.jpg")]
    [InlineData("ftp://example.invalid/cover.jpg")]
    public async Task A_cover_that_cannot_be_read_is_no_cover(string url)
        => Assert.Null(await LinuxMediaPlatform.LoadCoverAsync(url));
}

/// <summary>Where Linux desktops keep the Downloads folder.</summary>
public class LinuxShellTests
{
    [Fact]
    public void Downloads_folder_comes_from_user_dirs()
    {
        Assert.Equal("/home/me/Downloads", LinuxShellPlatform.DownloadDir(new[] { "# comment", "XDG_DESKTOP_DIR=\"$HOME/Desktop\"", "XDG_DOWNLOAD_DIR=\"$HOME/Downloads\"" }, "/home/me"));
        Assert.Equal("/data/Herunterladen", LinuxShellPlatform.DownloadDir(new[] { "XDG_DOWNLOAD_DIR=\"/data/Herunterladen/\"" }, "/home/me"));
        Assert.Null(LinuxShellPlatform.DownloadDir(new[] { "XDG_DOWNLOAD_DIR=\"relative\"" }, "/home/me"));
        Assert.Null(LinuxShellPlatform.DownloadDir(Array.Empty<string>(), "/home/me"));
    }
}

/// <summary>Which folders are mounted drives (Linux) and which volumes are external (macOS).</summary>
public class RemovableDriveTests : IDisposable
{
    readonly string _mounts = Path.GetTempFileName();

    public void Dispose() => File.Delete(_mounts);

    [Fact]
    public void A_folder_is_a_drive_once_it_is_a_mount_point()
    {
        File.WriteAllLines(_mounts, new[]
        {
            "/dev/sda2 / ext4 rw 0 0",
            "/dev/sdb1 /run/media/me/MY\\040STICK vfat rw,nosuid 0 0",
        });
        Assert.True(Holm.Platform.Linux.LinuxRemovableDrivesPlatform.IsMountPoint("/run/media/me/MY STICK", _mounts));
        Assert.False(Holm.Platform.Linux.LinuxRemovableDrivesPlatform.IsMountPoint("/run/media/me/OTHER", _mounts));
    }

    [Theory]
    [InlineData("   Device Location:           External\n   Protocol:                  USB", true)]
    [InlineData("   Device Location:           Internal\n   Protocol:                  Apple Fabric", false)]
    [InlineData("   Protocol:                  Disk Image\n   Internal:                  No", false)]
    [InlineData("   Internal:                  No", true)]
    [InlineData("", false)]
    public void Only_external_physical_volumes_count_on_macOS(string info, bool external)
        => Assert.Equal(external, Holm.Platform.MacOS.MacRemovableDrivesPlatform.IsExternal(info));
}
