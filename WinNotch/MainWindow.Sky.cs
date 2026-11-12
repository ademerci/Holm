using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinNotch;

/// <summary>The colours of the sky behind the open notch: two soft glows at the top, and stars at night.</summary>
internal readonly record struct SkyLook(Color Left, Color Right, bool Stars, string Name);

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
                        : new(Rgb(0x2F, 0x86, 0xFF), Rgb(0x2C, 0xC2, 0xD6), false, "day"),
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

    static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}

// Nothing playing, the open notch gets a sky: soft light at the top in the colours of the time of day and the weather
// (stars at night), a greeting over the clock, and the clock rolls over to the next minute. All of it is drawn once
// and stays still, so it costs nothing while you look at it.
public partial class MainWindow
{
    const double SkyWidth = 540, SkyHeight = 420; // the open notch with its shoulders, roughly
    SkyLook? _skyShown;
    bool _skyOn;
    readonly TranslateTransform _clockRoll = new();

    void InitSky() => ClockBig.RenderTransform = _clockRoll;

    /// <summary>Shows the sky while the open notch is on home without music, and takes it away otherwise.</summary>
    void ShowSky()
    {
        bool on = _expanded && !_hiddenForFullscreen && (_settings.MediaControls ? _info : null) == null;
        if (on)
        {
            var weather = _settings.Weather ? _weather : null;
            var look = Sky.For(DateTime.Now, weather?.Kind, weather?.IsDay);
            if (look != _skyShown)
            {
                SkyLayer.Background = Paint(look);
                GreetingText.Foreground = Frozen(Mix(look.Left, Colors.White, 0.55));
                _skyShown = look;
            }
        }
        GreetingText.Text = Sky.Greeting(DateTime.Now, _settings.ProfileName);
        if (on == _skyOn) return;
        _skyOn = on;
        if (!_settings.Animations) { SkyLayer.BeginAnimation(OpacityProperty, null); SkyLayer.Opacity = on ? 1 : 0; return; }
        SkyLayer.BeginAnimation(OpacityProperty, on
            ? new DoubleAnimation(1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = FadeInEase }
            : new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = FadeOut });
    }

    /// <summary>The clock turns over to a new minute: the new time rolls up into place.</summary>
    void RollClock()
    {
        if (!_settings.Animations || !_expanded || ClockPanel.Visibility != Visibility.Visible) return;
        var roll = new DoubleAnimation(9, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = Spring.Bouncy, FillBehavior = FillBehavior.Stop };
        _clockRoll.BeginAnimation(TranslateTransform.YProperty, roll);
        ClockBig.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(260)) { FillBehavior = FillBehavior.Stop });
    }

    static Brush Paint(SkyLook look)
    {
        var sky = new DrawingGroup();
        var area = new RectangleGeometry(new Rect(0, 0, SkyWidth, SkyHeight));
        // Light from the top left, over the clock, and a second colour from the top right, over the weather.
        sky.Children.Add(new GeometryDrawing(Glow(look.Left, 0.46, new Point(120, -30), 330, 250), null, area));
        sky.Children.Add(new GeometryDrawing(Glow(look.Right, 0.30, new Point(470, -20), 270, 200), null, area));
        if (look.Stars)
        {
            // The same stars every night, so the sky doesn't change each time it opens.
            var random = new Random(7);
            for (int i = 0; i < 16; i++)
            {
                var at = new Point(30 + random.NextDouble() * (SkyWidth - 60), 8 + random.NextDouble() * 150);
                double size = 0.55 + random.NextDouble() * 0.7;
                var star = new SolidColorBrush(Color.FromArgb((byte)(70 + random.Next(110)), 255, 255, 255));
                sky.Children.Add(new GeometryDrawing(star, null, new EllipseGeometry(at, size, size)));
            }
        }
        var brush = new DrawingBrush(sky)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, SkyWidth, SkyHeight),
        };
        brush.Freeze();
        return brush;
    }

    static RadialGradientBrush Glow(Color color, double strength, Point center, double rx, double ry)
    {
        Color At(double alpha) => Color.FromArgb((byte)Math.Round(255 * strength * alpha), color.R, color.G, color.B);
        return new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = rx,
            RadiusY = ry,
            GradientStops =
            {
                new GradientStop(At(1), 0),
                new GradientStop(At(0.45), 0.45),
                new GradientStop(At(0.12), 0.75),
                new GradientStop(At(0), 1),
            },
        };
    }

    static Color Mix(Color a, Color b, double t)
        => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
