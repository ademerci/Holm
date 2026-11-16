using System;
using System.Linq;

namespace WinNotch;

/// <summary>The colours of the sky behind the open notch: two soft glows at the top, and stars at night.</summary>
internal readonly record struct SkyLook(SkyColor Left, SkyColor Right, bool Stars, string Name);

/// <summary>A colour of the sky, without a UI framework's colour type, so this file builds anywhere.</summary>
internal readonly record struct SkyColor(byte R, byte G, byte B);

/// <summary>What the sky looks like at a time of day and in some weather, and how the notch says hello.</summary>
internal static class Sky
{
    public static SkyLook For(DateTime local, WeatherKind? weather, bool? isDay)
    {
        int h = local.Hour;
        // The weather knows when the sun is up; without it, the clock guesses.
        bool day = isDay ?? h is >= 7 and < 19;
        string phase = !day ? "night" : h < 9 ? "dawn" : h >= 17 ? "dusk" : "day";

        switch (weather)
        {
            case WeatherKind.Rain or WeatherKind.Drizzle or WeatherKind.Thunder:
                return day ? new(Rgb(0x3E, 0x6E, 0xB8), Rgb(0x5A, 0x7F, 0xA8), false, "rain")
                           : new(Rgb(0x26, 0x3A, 0x78), Rgb(0x3A, 0x4E, 0x80), false, "rain at night");
            case WeatherKind.Snow:
                return new(Rgb(0x8F, 0xBC, 0xF5), Rgb(0xC9, 0xDD, 0xF7), !day, day ? "snow" : "snow at night");
            case WeatherKind.Fog:
                return new(Rgb(0x70, 0x7C, 0x8C), Rgb(0x8A, 0x92, 0x9C), false, "fog");
        }
        bool clouds = weather is WeatherKind.Cloudy;
        return phase switch
        {
            "dawn" => new(Rgb(0xFF, 0x8A, 0x5C), Rgb(0xE8, 0x5C, 0x9A), false, "dawn"),
            "dusk" => new(Rgb(0xFF, 0x7A, 0x2F), Rgb(0xB0, 0x36, 0x8E), false, "dusk"),
            "night" => new(Rgb(0x3A, 0x3F, 0xC8), Rgb(0x6A, 0x3C, 0xB0), !clouds, "night"),
            _ => clouds ? new(Rgb(0x55, 0x80, 0xC0), Rgb(0x7D, 0x93, 0xB0), false, "clouds")
                        : new(Rgb(0x2F, 0x86, 0xFF), Rgb(0xB8, 0x96, 0x32), false, "day"),
        };
    }

    public static string Greeting(DateTime local, string? name)
    {
        string hello = local.Hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 18 => "Good afternoon",
            >= 18 and < 23 => "Good evening",
            _ => "Hello, night owl",
        };
        string? first = FirstName(name);
        return first == null || hello.EndsWith("owl") ? hello : $"{hello}, {first}";
    }

    /// <summary>The first word of a name, if it is one (letters, a hyphen or an apostrophe), at most 20 characters.</summary>
    public static string? FirstName(string? name)
    {
        string first = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length is > 0 and <= 20 && first.All(c => char.IsLetter(c) || c is '-' or '\'') ? first : null;
    }

    static SkyColor Rgb(byte r, byte g, byte b) => new(r, g, b);
}
