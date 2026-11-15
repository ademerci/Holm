using System.Windows;
using System.Windows.Media;

namespace WinNotch;

/// <summary>The WPF side of the clock view: swatches and fonts. The keys and formatting are in BigClock.cs.</summary>
internal static partial class BigClock
{
    public static readonly (string Key, string Label, Color Swatch)[] Colors =
    {
        (ColorWhite, "White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (ColorSky, "Auto", Color.FromRgb(0x5E, 0x9E, 0xFF)),
        (ColorAmber, "Amber", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        (ColorCyan, "Cyan", Color.FromRgb(0x64, 0xD2, 0xFF)),
        (ColorGreen, "Green", Color.FromRgb(0x30, 0xD1, 0x58)),
        (ColorRose, "Rose", Color.FromRgb(0xFF, 0x37, 0x5F)),
    };

    public static Color ResolveColor(string? colorKey, Color? mediaAccent, Color? skyAccent)
    {
        string norm = NormalizeColor(colorKey);
        if (norm == ColorSky)
            return mediaAccent ?? skyAccent ?? System.Windows.Media.Colors.White;
        foreach (var (k, _, swatch) in Colors)
            if (k == norm) return swatch;
        return System.Windows.Media.Colors.White;
    }

    public static (FontFamily Family, FontWeight Weight) ResolveFont(string? style) => NormalizeStyle(style) switch
    {
        StyleBold => (new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeights.Bold),
        StyleMono => (new FontFamily("Cascadia Mono, Consolas, Segoe UI"), FontWeights.SemiBold),
        _ => (new FontFamily("Segoe UI Variable Display, Segoe UI"), FontWeights.Light),
    };
}
