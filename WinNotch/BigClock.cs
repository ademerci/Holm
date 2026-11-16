using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WinNotch;

/// <summary>Formatting, styles and colour choices for the full-notch clock view.</summary>
internal static class BigClock
{
    public const string StyleLight = "light";
    public const string StyleBold = "bold";
    public const string StyleMono = "mono";

    public const string ColorWhite = "white";
    public const string ColorSky = "sky";
    public const string ColorAmber = "amber";
    public const string ColorCyan = "cyan";
    public const string ColorGreen = "green";
    public const string ColorRose = "rose";

    public static readonly (string Key, string Label, Color Swatch)[] Colors =
    {
        (ColorWhite, "White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        (ColorSky, "Auto", Color.FromRgb(0x5E, 0x9E, 0xFF)),
        (ColorAmber, "Amber", Color.FromRgb(0xFF, 0x9F, 0x0A)),
        (ColorCyan, "Cyan", Color.FromRgb(0x64, 0xD2, 0xFF)),
        (ColorGreen, "Green", Color.FromRgb(0x30, 0xD1, 0x58)),
        (ColorRose, "Rose", Color.FromRgb(0xFF, 0x37, 0x5F)),
    };

    public static string NormalizeStyle(string? style) => style?.ToLowerInvariant() switch
    {
        StyleBold => StyleBold,
        StyleMono => StyleMono,
        _ => StyleLight,
    };

    public static string NextStyle(string? current) => NormalizeStyle(current) switch
    {
        StyleLight => StyleBold,
        StyleBold => StyleMono,
        _ => StyleLight,
    };

    public static string StyleLabel(string? style) => NormalizeStyle(style) switch
    {
        StyleBold => "Bold",
        StyleMono => "Mono",
        _ => "Light",
    };

    public static string NormalizeColor(string? color)
    {
        string key = (color ?? "").Trim().ToLowerInvariant();
        foreach (var (k, _, _) in Colors)
            if (k == key) return k;
        return ColorWhite;
    }

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

    public static string FormatTime(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string FormatSeconds(DateTime local) => local.ToString(":ss", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime local) => local.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"));
}
