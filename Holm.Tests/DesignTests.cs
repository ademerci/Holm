using System.Linq;
using WinNotch;
using Xunit;

namespace Holm.Tests;

public class DesignTests
{
    [Fact]
    public void Every_built_in_design_has_its_own_id_and_readable_ink()
    {
        Assert.Equal(17, Designs.BuiltIn.Count);
        Assert.Equal(Designs.BuiltIn.Count, Designs.BuiltIn.Select(d => d.Id).Distinct().Count());
        foreach (var d in Designs.BuiltIn)
        {
            Assert.True(Designs.Contrast(d.Ink, d.Panel) >= 4.5, $"{d.Id}: ink on panel");
            Assert.True(Designs.Contrast(d.Ink, d.Fill[0]) >= 3, $"{d.Id}: ink on notch");
            // A see-through colour sits on the panel, so only an opaque one can hide the text on its own.
            if (d.SelectedColor >> 24 == 0xFF) Assert.True(Designs.Contrast(d.Ink, d.SelectedColor) >= 3, $"{d.Id}: ink on the selected row");
            if (d.CardColor >> 24 == 0xFF) Assert.True(Designs.Contrast(d.Ink, d.CardColor) >= 4.5, $"{d.Id}: ink on cards");
        }
    }

    [Fact]
    public void Classic_stays_the_default_and_the_old_instrument_switch_still_counts()
    {
        Assert.Equal("classic", Designs.Current(new AppSettings()).Id);
        Assert.Equal("instrument", Designs.Current(new AppSettings { InstrumentLook = true }).Id);
        Assert.Equal("neon", Designs.Current(new AppSettings { InstrumentLook = true, Design = "neon" }).Id);
        Assert.Equal("classic", Designs.Current(new AppSettings { Design = "gone" }).Id);
    }

    [Fact]
    public void The_chosen_accent_replaces_the_designs_own()
    {
        var d = Designs.Current(new AppSettings { Design = "classic", AccentColor = "#FF9F0A" });
        Assert.Equal(0xFFFF9F0Au, d.Accent);
        Assert.Equal(0xFFFF9F0Au, d.ToggleColor); // switches follow the chosen accent
        Assert.Equal(0xFF30D158u, Designs.Find("classic").ToggleColor); // Classic's own switches stay green
        Assert.Equal(0xFF0A84FFu, Designs.Current(new AppSettings { AccentColor = "nonsense" }).Accent);
    }

    [Fact]
    public void An_imported_design_starts_from_its_base_and_keeps_values_in_range()
    {
        Assert.True(Designs.Parse("""
            { "name": "Ocean Night!", "base": "instrument", "fill": ["#02060A", "#0B1620"], "fillKind": "diagonal",
              "accent": "#32ADE6", "corners": 99, "edgeWidth": -3, "font": "mono", "unknown": 1, }
            """, out var d, out string? error), error);
        Assert.Equal("custom-ocean-night", d!.Id);
        Assert.True(d.Instrument); // from the base
        Assert.Equal(new[] { 0xFF02060Au, 0xFF0B1620u }, d.Fill);
        Assert.Equal(30, d.Corners);
        Assert.Equal(0, d.EdgeWidth);
        Assert.Equal("mono", d.Font);
    }

    [Theory]
    [InlineData("""{ "fill": "#000" }""", "name")]
    [InlineData("""{ "name": "x", "accent": "blue" }""", "accent")]
    [InlineData("""{ "name": "x", "font": "Comic Sans" }""", "font")]
    [InlineData("""{ "name": "x", "base": "nope" }""", "base")]
    [InlineData("""{ "name": "x", "ink": "#1E1E20", "panel": "#1E1E20" }""", "read")]
    [InlineData("""[1, 2]""", "object")]
    [InlineData("""{ "name": """, "JSON")]
    public void A_broken_design_says_what_is_wrong(string json, string hint)
    {
        Assert.False(Designs.Parse(json, out var d, out string? error));
        Assert.Null(d);
        Assert.Contains(hint, error);
    }

    [Theory]
    [InlineData("#FFF", 0xFFFFFFFFu)]
    [InlineData("#0A84FF", 0xFF0A84FFu)]
    [InlineData("#800A84FF", 0x800A84FFu)]
    public void Colours_read_in_three_lengths(string text, uint argb)
    {
        Assert.True(Designs.TryColor(text, out uint c));
        Assert.Equal(argb, c);
    }

    [Fact]
    public void Text_on_the_accent_is_white_unless_it_cannot_be_read()
    {
        Assert.Equal(0xFFFFFFFFu, Designs.OnColor(0xFF0A84FF));
        Assert.Equal(0xFF0B0B0Cu, Designs.OnColor(0xFFF2F2F2));
        Assert.Equal(0xFF0B0B0Cu, Designs.OnColor(0xFFFFD60A));
    }
}
