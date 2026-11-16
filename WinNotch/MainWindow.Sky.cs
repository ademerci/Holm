using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinNotch;

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
        bool on = _settings.SkyColors && _expanded && !_hiddenForFullscreen && ((_settings.MediaControls ? _info : null) == null || _clockViewOpen);
        if (on)
        {
            var weather = _settings.Weather ? _weather : null;
            var look = Sky.For(DateTime.Now, weather?.Kind, weather?.IsDay);
            if (look != _skyShown)
            {
                SkyLayer.Background = Paint(look);
                GreetingText.Foreground = Frozen(Mix(ToColor(look.Left), Colors.White, 0.55));
                _skyShown = look;
                if (_clockViewOpen) RefreshBigClockStyle();
            }
        }
        if (!_settings.SkyColors) GreetingText.Foreground = DimBrush; // no sky, no tint
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
        sky.Children.Add(new GeometryDrawing(Glow(ToColor(look.Left), 0.46, new Point(120, -30), 330, 250), null, area));
        sky.Children.Add(new GeometryDrawing(Glow(ToColor(look.Right), 0.30, new Point(470, -20), 270, 200), null, area));
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

    static Color ToColor(SkyColor c) => Color.FromRgb(c.R, c.G, c.B);

    static Color Mix(Color a, Color b, double t)
        => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
