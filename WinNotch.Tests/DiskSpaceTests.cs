using System.Text.Json;

namespace WinNotch.Tests;

public class DiskSpaceTests
{
    const long Gb = DiskSpace.Gb;

    [Theory]
    [InlineData(9, 1000, true)]      // under 10 GB
    [InlineData(50, 1000, true)]     // 5 %
    [InlineData(99, 1000, true)]     // just under 10 %
    [InlineData(100, 1000, false)]
    [InlineData(10, 64, false)]      // a small drive: 10 GB and 15 %
    [InlineData(9, 64, true)]
    public void Low_means_under_ten_percent_or_ten_gigabytes(long freeGb, long totalGb, bool low)
        => Assert.Equal(low, DiskSpace.IsLow(freeGb * Gb, totalGb * Gb));

    [Fact]
    public void An_unknown_drive_is_never_low() => Assert.False(DiskSpace.IsLow(0, 0));

    [Fact]
    public void Warns_once_per_low_spell()
    {
        bool warned = false;
        Assert.False(DiskSpace.ShouldWarn(200 * Gb, 1000 * Gb, ref warned));
        Assert.True(DiskSpace.ShouldWarn(80 * Gb, 1000 * Gb, ref warned));
        Assert.True(warned);
        Assert.False(DiskSpace.ShouldWarn(70 * Gb, 1000 * Gb, ref warned));   // still low: told already
        Assert.False(DiskSpace.ShouldWarn(110 * Gb, 1000 * Gb, ref warned));  // 11 %: not low, not roomy yet
        Assert.False(DiskSpace.ShouldWarn(95 * Gb, 1000 * Gb, ref warned));   // so hovering doesn't nag
        Assert.False(DiskSpace.ShouldWarn(130 * Gb, 1000 * Gb, ref warned));  // room again
        Assert.False(warned);
        Assert.True(DiskSpace.ShouldWarn(90 * Gb, 1000 * Gb, ref warned));    // a new low spell
    }

    [Fact]
    public void Remembered_across_a_restart_through_the_flag()
    {
        bool warned = true; // from the settings
        Assert.False(DiskSpace.ShouldWarn(50 * Gb, 1000 * Gb, ref warned));
    }

    [Theory]
    [InlineData(8.4, 100, "8.4 GB free · 8 %")]
    [InlineData(23.9, 256, "23 GB free · 9 %")]
    [InlineData(0.2, 128, "0.2 GB free · 0 %")]
    public void Describes_free_space(double freeGb, long totalGb, string expected)
        => Assert.Equal(expected, DiskSpace.Describe((long)(freeGb * Gb), totalGb * Gb));

    [Fact]
    public void Settings_from_before_the_disk_warning_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"CapsLock\":false}")!;
        Assert.False(old.CapsLock);
        Assert.True(old.DiskWarning);
        Assert.False(old.DiskWarned);
    }
}
