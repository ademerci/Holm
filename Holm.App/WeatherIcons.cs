using System;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The weather symbols, drawn as thin lines in a 16 × 16 box like the notch's other icons: the WPF app's
/// WeatherIcons.cs, the same paths, on Avalonia.
/// </summary>
static class WeatherIcons
{
    const string CloudPath = "M4.5,12.5 H11.8 A2.7,2.7 0 0 0 12,7.1 A4,4 0 0 0 4.6,7.8 A2.35,2.35 0 0 0 4.5,12.5 Z";

    public static Path Make(WeatherKind kind, bool day, double size, IBrush brush) => new()
    {
        Data = Geometry(kind, day),
        Stroke = brush,
        StrokeThickness = 1.3,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Stretch = Stretch.None,
        Width = 16,
        Height = 16,
        RenderTransform = new ScaleTransform(size / 16, size / 16),
        RenderTransformOrigin = RelativePoint.TopLeft,
        Margin = new Thickness(0, 0, size - 16, size - 16), // takes the scaled size in the layout
        VerticalAlignment = VerticalAlignment.Center,
    };

    public static Geometry Geometry(WeatherKind kind, bool day)
    {
        var group = new GeometryGroup();
        switch (kind)
        {
            case WeatherKind.Clear when day:
                Sun(group, 8, 8, 3, 5, 6.6);
                break;
            case WeatherKind.Clear:
                group.Children.Add(Avalonia.Media.Geometry.Parse("M10,2.6 A5.6,5.6 0 1 0 13.4,11.2 A4.6,4.6 0 0 1 10,2.6 Z"));
                break;
            case WeatherKind.PartlyCloudy:
                if (day) Sun(group, 5.6, 5.4, 2, 3.4, 4.5);
                else group.Children.Add(Avalonia.Media.Geometry.Parse("M5.6,1.8 A3.6,3.6 0 1 0 8.6,6.6 A2.8,2.8 0 0 1 5.6,1.8 Z"));
                group.Children.Add(Cloud(1.6, 1.8, 0.85));
                break;
            case WeatherKind.Cloudy:
                group.Children.Add(Cloud(0, 0, 1));
                break;
            case WeatherKind.Fog:
                group.Children.Add(Avalonia.Media.Geometry.Parse("M2.5,5.5 H13.5 M4,8.5 H12 M2.5,11.5 H13.5"));
                break;
            case WeatherKind.Drizzle:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(Avalonia.Media.Geometry.Parse("M6,12.2 L5.5,13.6 M10,12.2 L9.5,13.6"));
                break;
            case WeatherKind.Rain:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(Avalonia.Media.Geometry.Parse("M5,12 L4,14.6 M8,12 L7,14.6 M11,12 L10,14.6"));
                break;
            case WeatherKind.Snow:
                group.Children.Add(Cloud(0, -2.2, 1));
                foreach (var (x, y) in new[] { (5.0, 13.0), (8.0, 14.4), (11.0, 13.0) })
                    group.Children.Add(new EllipseGeometry(new Rect(x - 0.55, y - 0.55, 1.1, 1.1)));
                break;
            case WeatherKind.Thunder:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(Avalonia.Media.Geometry.Parse("M8.6,11 L7,13.2 H9 L7.6,15.4"));
                break;
        }
        return group;
    }

    static void Sun(GeometryGroup group, double cx, double cy, double r, double inner, double outer)
    {
        group.Children.Add(new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r)));
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            group.Children.Add(new LineGeometry(new Point(cx + inner * Math.Cos(a), cy + inner * Math.Sin(a)),
                                                new Point(cx + outer * Math.Cos(a), cy + outer * Math.Sin(a))));
        }
    }

    static Geometry Cloud(double dx, double dy, double scale)
    {
        var cloud = Avalonia.Media.Geometry.Parse(CloudPath);
        // Scaled about the box's centre, then moved.
        cloud.Transform = new MatrixTransform(
            Matrix.CreateTranslation(-8, -8) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(8 + dx, 8 + dy));
        return cloud;
    }
}
