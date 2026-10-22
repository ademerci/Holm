using System.Text.Json;

namespace WinNotch.Tests;

public class SettingsTests
{
    [Fact]
    public void Missing_sizes_default_to_normal()
    {
        var settings = Read("{}");

        Assert.Equal(1, settings.CollapsedScale);
        Assert.Equal(1, settings.ExpandedScale);
    }

    [Theory]
    [InlineData(0.85)]
    [InlineData(1)]
    [InlineData(1.15)]
    [InlineData(1.3)]
    public void Legacy_shared_size_migrates_to_both_sizes(double scale)
    {
        var settings = Read(JsonSerializer.Serialize(new { Scale = scale }));

        Assert.Equal(scale, settings.CollapsedScale);
        Assert.Equal(scale, settings.ExpandedScale);
    }

    [Theory]
    [InlineData("{\"Scale\":1.15,\"CollapsedScale\":0.85}", 0.85, 1.15)]
    [InlineData("{\"CollapsedScale\":0.85,\"Scale\":1.15}", 0.85, 1.15)]
    [InlineData("{\"Scale\":1.15,\"ExpandedScale\":1.3}", 1.15, 1.3)]
    [InlineData("{\"ExpandedScale\":1.3,\"Scale\":1.15}", 1.15, 1.3)]
    [InlineData("{\"CollapsedScale\":0.85,\"ExpandedScale\":1.3,\"Scale\":1.15}", 0.85, 1.3)]
    public void Explicit_sizes_override_legacy_size_regardless_of_property_order(string json, double collapsed, double expanded)
    {
        var settings = Read(json);

        Assert.Equal(collapsed, settings.CollapsedScale);
        Assert.Equal(expanded, settings.ExpandedScale);
    }

    [Theory]
    [InlineData("{\"CollapsedScale\":0.85}", 0.85, 1)]
    [InlineData("{\"ExpandedScale\":1.3}", 1, 1.3)]
    public void Setting_one_size_leaves_the_other_at_normal(string json, double collapsed, double expanded)
    {
        var settings = Read(json);

        Assert.Equal(collapsed, settings.CollapsedScale);
        Assert.Equal(expanded, settings.ExpandedScale);
    }

    [Fact]
    public void Sizes_can_change_independently_and_survive_saving_without_legacy_size()
    {
        var settings = Read("{\"Scale\":1.15}");
        settings.CollapsedScale = 0.85;
        Assert.Equal(1.15, settings.ExpandedScale);
        settings.ExpandedScale = 1.3;
        Assert.Equal(0.85, settings.CollapsedScale);

        string json = JsonSerializer.Serialize(settings);
        using var saved = JsonDocument.Parse(json);
        Assert.False(saved.RootElement.TryGetProperty("Scale", out _));
        Assert.Equal(0.85, saved.RootElement.GetProperty("CollapsedScale").GetDouble());
        Assert.Equal(1.3, saved.RootElement.GetProperty("ExpandedScale").GetDouble());

        var reloaded = Read(json);
        Assert.Equal(0.85, reloaded.CollapsedScale);
        Assert.Equal(1.3, reloaded.ExpandedScale);
    }

    static AppSettings Read(string json) => JsonSerializer.Deserialize<AppSettings>(json)!;
}
