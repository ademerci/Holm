using System;
using System.Globalization;

namespace WinNotch;

/// <summary>Formatting, styles and colour choices for the full-notch clock view.</summary>
internal static partial class BigClock
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

    /// <summary>Every colour key, in the order the picker shows them.</summary>
    public static readonly string[] ColorKeys = { ColorWhite, ColorSky, ColorAmber, ColorCyan, ColorGreen, ColorRose };

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
        foreach (string k in ColorKeys)
            if (k == key) return k;
        return ColorWhite;
    }

    public static string FormatTime(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string FormatSeconds(DateTime local) => local.ToString(":ss", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime local) => local.ToString("dddd, MMMM d", CultureInfo.GetCultureInfo("en-US"));
}
