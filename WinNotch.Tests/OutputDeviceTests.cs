namespace WinNotch.Tests;

public class OutputDeviceTests
{
    [Theory]
    [InlineData(3, "")]  // headphones
    [InlineData(5, "")]  // headset
    [InlineData(6, "")]  // handset
    [InlineData(9, "")]  // HDMI display
    [InlineData(1, "")]  // speakers
    [InlineData(10, "")] // unknown
    public void Each_kind_of_device_has_its_icon(int formFactor, string glyph)
        => Assert.Equal(glyph, new OutputDevice("id", "Speakers", formFactor).Glyph);

    /// <summary>Only reads this PC's playback devices; the default stays as it is.</summary>
    [Fact, Trait("Category", "Local")]
    public void Lists_this_PCs_playback_devices_with_the_default_among_them()
    {
        var volume = new VolumeService();
        volume.Bind();
        try
        {
            var outputs = volume.Outputs(out string? current);
            Assert.NotEmpty(outputs);
            Assert.Contains(outputs, o => o.Id == current);
            Assert.All(outputs, o => Assert.False(string.IsNullOrWhiteSpace(o.Name)));
            Assert.Equal(outputs.Count, outputs.Select(o => o.Id).Distinct().Count());
        }
        finally { volume.Shutdown(); }
    }

    /// <summary>
    /// Makes each role's default device the default again: that goes through the same undocumented interface as
    /// switching, so a wrong method table shows up, while nothing on this PC changes.
    /// </summary>
    [Fact, Trait("Category", "Local")]
    public void Setting_the_default_device_works_and_changes_nothing_here()
    {
        var volume = new VolumeService();
        volume.Bind();
        try
        {
            for (int role = 0; role < 3; role++)
            {
                string? before = volume.DefaultId(role);
                Assert.NotNull(before);
                Assert.True(volume.SetDefault(before!, role));
                Assert.Equal(before, volume.DefaultId(role));
            }
        }
        finally { volume.Shutdown(); }
    }

    [Fact]
    public void Lists_nothing_before_it_is_bound()
        => Assert.Empty(new VolumeService().Outputs(out _));
}
