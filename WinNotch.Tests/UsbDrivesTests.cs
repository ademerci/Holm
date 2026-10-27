using System.Text.Json;

namespace WinNotch.Tests;

public class UsbDrivesTests
{
    [Fact]
    public void Reads_the_letters_of_a_unit_mask()
    {
        Assert.Equal(new[] { 'E' }, UsbDrives.Letters(1u << 4));
        Assert.Equal(new[] { 'A', 'C', 'Z' }, UsbDrives.Letters(1u | 1u << 2 | 1u << 25));
        Assert.Empty(UsbDrives.Letters(0));
        Assert.Empty(UsbDrives.Letters(1u << 26)); // beyond Z
    }

    [Theory]
    [InlineData(32_000_000_000L, 12_884_901_888L, "29.8 GB · 12 GB free")]
    [InlineData(128_000_000_000L, 0L, "119 GB")]
    [InlineData(512L * 1024 * 1024, 100L * 1024 * 1024, "512 MB · 100 MB free")]
    [InlineData(0L, 0L, "Removable drive")]
    public void Describes_size_and_free_space(long total, long free, string expected) => Assert.Equal(expected, UsbDrives.Describe(total, free));

    [Fact]
    public void Names_a_drive_by_label_and_letter()
    {
        var drive = new UsbDrive('E', "KINGSTON", 32_000_000_000L, 12_884_901_888L);
        Assert.Equal("KINGSTON (E:)", drive.Name);
        Assert.Equal(@"E:\", drive.Root);
    }

    [Fact]
    public void Settings_from_before_usb_drives_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"DiskWarning\":false}")!;
        Assert.False(old.DiskWarning);
        Assert.True(old.UsbDrives);
    }
}
