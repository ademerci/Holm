using System;
using System.Windows;
using System.Windows.Media;

namespace WinNotch.Tests;

public class BigClockTests
{
    [Fact]
    public void Formats_time_seconds_and_date()
    {
        var dt = new DateTime(2026, 11, 15, 20, 18, 9);
        Assert.Equal("20:18", BigClock.FormatTime(dt));
        Assert.Equal(":09", BigClock.FormatSeconds(dt));
        Assert.Equal("Sunday, November 15", BigClock.FormatDate(dt));
    }

    [Fact]
    public void Cycles_styles_and_normalizes_unknown_values()
    {
        Assert.Equal(BigClock.StyleLight, BigClock.NormalizeStyle(null));
        Assert.Equal(BigClock.StyleLight, BigClock.NormalizeStyle("unknown"));
        Assert.Equal(BigClock.StyleBold, BigClock.NextStyle(BigClock.StyleLight));
        Assert.Equal(BigClock.StyleMono, BigClock.NextStyle(BigClock.StyleBold));
        Assert.Equal(BigClock.StyleLight, BigClock.NextStyle(BigClock.StyleMono));

        Assert.Equal("Light", BigClock.StyleLabel(BigClock.StyleLight));
        Assert.Equal("Bold", BigClock.StyleLabel(BigClock.StyleBold));
        Assert.Equal("Mono", BigClock.StyleLabel(BigClock.StyleMono));
    }

    [Fact]
    public void Resolves_fonts_and_colors()
    {
        var (_, lightWeight) = BigClock.ResolveFont(BigClock.StyleLight);
        var (_, boldWeight) = BigClock.ResolveFont(BigClock.StyleBold);
        var (monoFamily, monoWeight) = BigClock.ResolveFont(BigClock.StyleMono);

        Assert.Equal(FontWeights.Light, lightWeight);
        Assert.Equal(FontWeights.Bold, boldWeight);
        Assert.Equal(FontWeights.SemiBold, monoWeight);
        Assert.Contains("Cascadia Mono", monoFamily.Source);

        var mediaColor = Color.FromRgb(0x12, 0x34, 0x56);
        var skyColor = Color.FromRgb(0x65, 0x43, 0x21);
        Assert.Equal(mediaColor, BigClock.ResolveColor(BigClock.ColorSky, mediaColor, skyColor));
        Assert.Equal(skyColor, BigClock.ResolveColor(BigClock.ColorSky, null, skyColor));
        Assert.Equal(Colors.White, BigClock.ResolveColor(BigClock.ColorSky, null, null));
        Assert.Equal(Color.FromRgb(0xFF, 0x9F, 0x0A), BigClock.ResolveColor(BigClock.ColorAmber, mediaColor, skyColor));
        Assert.Equal(Colors.White, BigClock.ResolveColor("unknown", mediaColor, skyColor));
    }

    [Fact]
    public void Settings_default_to_showing_media_and_seconds_and_date()
    {
        var settings = new AppSettings();
        Assert.True(settings.ClockShowMedia);
        Assert.True(settings.ClockShowSeconds);
        Assert.True(settings.ClockShowDate);
        Assert.Equal(BigClock.StyleLight, settings.ClockStyle);
        Assert.Equal(BigClock.ColorWhite, settings.ClockColor);
    }

    [Fact]
    public void MainWindow_xaml_has_only_valid_wpf_alignments()
    {
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WinNotch", "MainWindow.xaml"));
        var doc = System.Xml.Linq.XDocument.Load(path);
        foreach (var el in doc.Descendants())
        {
            if (el.Attribute("VerticalAlignment")?.Value is { } va && !va.StartsWith("{"))
                Assert.True(Enum.TryParse<VerticalAlignment>(va, out _), $"Invalid VerticalAlignment '{va}' on <{el.Name.LocalName}>");
            if (el.Attribute("HorizontalAlignment")?.Value is { } ha && !ha.StartsWith("{"))
                Assert.True(Enum.TryParse<HorizontalAlignment>(ha, out _), $"Invalid HorizontalAlignment '{ha}' on <{el.Name.LocalName}>");
        }
    }
}
