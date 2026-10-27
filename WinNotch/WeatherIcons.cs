using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinNotch;

/// <summary>
/// The weather symbols, drawn as thin lines in a 16 × 16 box like the notch's other icons. Drawn rather than taken
/// from a font: Windows' icon fonts have no dependable weather set.
/// </summary>
internal static class WeatherIcons
{
    const string CloudPath = "M4.5,12.5 H11.8 A2.7,2.7 0 0 0 12,7.1 A4,4 0 0 0 4.6,7.8 A2.35,2.35 0 0 0 4.5,12.5 Z";

    public static Path Make(WeatherKind kind, bool day, double size, Brush brush) => new()
    {
        Data = Geometry(kind, day),
        Stroke = brush,
        StrokeThickness = 1.3,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Stretch = Stretch.None,
        Width = 16,
        Height = 16,
        LayoutTransform = new ScaleTransform(size / 16, size / 16),
        VerticalAlignment = VerticalAlignment.Center,
        SnapsToDevicePixels = false,
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
                group.Children.Add(System.Windows.Media.Geometry.Parse("M10,2.6 A5.6,5.6 0 1 0 13.4,11.2 A4.6,4.6 0 0 1 10,2.6 Z"));
                break;
            case WeatherKind.PartlyCloudy:
                if (day) Sun(group, 5.6, 5.4, 2, 3.4, 4.5);
                else group.Children.Add(System.Windows.Media.Geometry.Parse("M5.6,1.8 A3.6,3.6 0 1 0 8.6,6.6 A2.8,2.8 0 0 1 5.6,1.8 Z"));
                group.Children.Add(Cloud(1.6, 1.8, 0.85));
                break;
            case WeatherKind.Cloudy:
                group.Children.Add(Cloud(0, 0, 1));
                break;
            case WeatherKind.Fog:
                group.Children.Add(System.Windows.Media.Geometry.Parse("M2.5,5.5 H13.5 M4,8.5 H12 M2.5,11.5 H13.5"));
                break;
            case WeatherKind.Drizzle:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(System.Windows.Media.Geometry.Parse("M6,12.2 L5.5,13.6 M10,12.2 L9.5,13.6"));
                break;
            case WeatherKind.Rain:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(System.Windows.Media.Geometry.Parse("M5,12 L4,14.6 M8,12 L7,14.6 M11,12 L10,14.6"));
                break;
            case WeatherKind.Snow:
                group.Children.Add(Cloud(0, -2.2, 1));
                foreach (var (x, y) in new[] { (5.0, 13.0), (8.0, 14.4), (11.0, 13.0) })
                    group.Children.Add(new EllipseGeometry(new Point(x, y), 0.55, 0.55));
                break;
            case WeatherKind.Thunder:
                group.Children.Add(Cloud(0, -2.2, 1));
                group.Children.Add(System.Windows.Media.Geometry.Parse("M8.6,11 L7,13.2 H9 L7.6,15.4"));
                break;
        }
        group.Freeze();
        return group;
    }

    static void Sun(GeometryGroup group, double cx, double cy, double r, double inner, double outer)
    {
        group.Children.Add(new EllipseGeometry(new Point(cx, cy), r, r));
        for (int i = 0; i < 8; i++)
        {
            double a = i * System.Math.PI / 4;
            group.Children.Add(new LineGeometry(new Point(cx + inner * System.Math.Cos(a), cy + inner * System.Math.Sin(a)),
                                                new Point(cx + outer * System.Math.Cos(a), cy + outer * System.Math.Sin(a))));
        }
    }

    static Geometry Cloud(double dx, double dy, double scale)
    {
        var cloud = System.Windows.Media.Geometry.Parse(CloudPath).Clone();
        var transform = new TransformGroup();
        transform.Children.Add(new ScaleTransform(scale, scale, 8, 8));
        transform.Children.Add(new TranslateTransform(dx, dy));
        cloud.Transform = transform;
        return cloud;
    }
}
