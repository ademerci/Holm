using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Nothing playing, the open notch gets a sky (the WPF app's MainWindow.Sky.cs, with Sky.cs linked): soft light at
/// the top in the colours of the time of day and the weather, stars at night, and a greeting over the clock. Drawn
/// once and still, so it costs nothing while you look at it.
/// </summary>
public sealed partial class NotchWindow
{
    readonly SkyLayer _sky = new() { Opacity = 0, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    // The instrument look has no greeting.
    readonly TextBlock _greeting = new() { IsVisible = !Look.Instrument, FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(1, -2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Dim };
    SkyLook? _skyShown;
    bool _skyOn;

    void BuildSky(StackPanel clockStack)
    {
        _backdrop.Children.Insert(1, _sky); // over the black, under the cover's colours
        clockStack.Children.Insert(0, _greeting);
    }

    /// <summary>Shows the sky while the open notch is on home without music, and takes it away otherwise.</summary>
    void ShowSky()
    {
        bool on = _settings.SkyColors && Look.Current.Wash && _state == NotchState.Expanded && (_settings.MediaControls ? _info : null) == null;
        if (on)
        {
            var weather = _settings.Weather ? _weather : null;
            var look = Sky.For(DateTime.Now, weather?.Kind, weather?.IsDay);
            if (look != _skyShown)
            {
                _sky.Look = look;
                _greeting.Foreground = new SolidColorBrush(Mix(look.Left, 0.55)).ToImmutable();
                _skyShown = look;
            }
        }
        if (!_settings.SkyColors) _greeting.Foreground = Dim; // no sky, no tint
        _greeting.Text = Sky.Greeting(DateTime.Now, _settings.ProfileName);
        if (on == _skyOn) return;
        _skyOn = on;
        _sky.Transitions = _settings.Animations
            ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(on ? 520 : 160), Delay = TimeSpan.FromMilliseconds(on ? 60 : 0), Easing = new Avalonia.Animation.Easings.CubicEaseOut() } }
            : null;
        _sky.Opacity = on ? 1 : 0;
    }

    void SetSkyColors(bool on)
    {
        _settings.SkyColors = on;
        _settings.Save();
        _skyShown = null;
        ShowSky();
    }

    /// <summary>A colour of the sky mixed toward white.</summary>
    static Color Mix(SkyColor a, double t)
        => Color.FromRgb((byte)(a.R + (255 - a.R) * t), (byte)(a.G + (255 - a.G) * t), (byte)(a.B + (255 - a.B) * t));

    /// <summary>The sky itself: two soft glows at the top and, at night, the same sixteen stars every time.</summary>
    sealed class SkyLayer : Control
    {
        const double SkyWidth = 540, SkyHeight = 420; // the open notch with its shoulders, roughly
        SkyLook? _look;

        public SkyLook? Look
        {
            get => _look;
            set { _look = value; InvalidateVisual(); }
        }

        protected override Size MeasureOverride(Size availableSize) => new(SkyWidth, SkyHeight);

        public override void Render(DrawingContext dc)
        {
            if (_look is not { } look) return;
            // Light from the top left, over the clock, and a second colour from the top right, over the weather.
            Glow(dc, look.Left, 0.46, new Point(120, -30), 330, 250);
            Glow(dc, look.Right, 0.30, new Point(470, -20), 270, 200);
            if (!look.Stars) return;
            var random = new Random(7);
            for (int i = 0; i < 16; i++)
            {
                var at = new Point(30 + random.NextDouble() * (SkyWidth - 60), 8 + random.NextDouble() * 150);
                double size = 0.55 + random.NextDouble() * 0.7;
                var star = new SolidColorBrush(Color.FromArgb((byte)(70 + random.Next(110)), 255, 255, 255));
                dc.DrawEllipse(star, null, at, size, size);
            }
        }

        /// <summary>An elliptic glow, fading out from its centre, as WPF's RadialGradientBrush draws it.</summary>
        static void Glow(DrawingContext dc, SkyColor color, double strength, Point center, double rx, double ry)
        {
            Color At(double alpha) => Color.FromArgb((byte)Math.Round(255 * strength * alpha), color.R, color.G, color.B);
            var brush = new RadialGradientBrush
            {
                Center = RelativePoint.Center, GradientOrigin = RelativePoint.Center,
                RadiusX = RelativeScalar.Middle, RadiusY = RelativeScalar.Middle,
                GradientStops = { new GradientStop(At(1), 0), new GradientStop(At(0.45), 0.45), new GradientStop(At(0.12), 0.75), new GradientStop(At(0), 1) },
            };
            dc.DrawEllipse(brush, null, center, rx, ry);
        }
    }
}
