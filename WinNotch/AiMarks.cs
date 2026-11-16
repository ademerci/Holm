using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace WinNotch;

/// <summary>
/// The marks of the coding agents, drawn as strokes on a 24 × 24 grid so they stay crisp at 10 px: Claude's spark
/// (rays of uneven length and angle with round ends, in its terracotta) and OpenAI's knot for Codex (six linked
/// rounded links around a hexagonal opening).
/// </summary>
internal static class AiMarks
{
    public static readonly Color ClaudeColor = Color.FromRgb(0xD9, 0x77, 0x57);

    // Angle (degrees from twelve o'clock, clockwise) and length of each ray, uneven like the hand-cut original.
    static readonly (double Angle, double Length)[] Rays =
    {
        (-4, 10.6), (27, 8.4), (52, 10.2), (83, 9.0), (113, 10.8), (141, 8.2),
        (168, 10.4), (197, 9.4), (226, 10.9), (254, 8.0), (284, 10.1), (313, 8.8), (338, 9.7),
    };

    static readonly Lazy<Geometry> Spark = new(() =>
    {
        var group = new GeometryGroup();
        foreach (var (angle, length) in Rays)
        {
            double a = angle * Math.PI / 180;
            var direction = new Vector(Math.Sin(a), -Math.Cos(a));
            group.Children.Add(new LineGeometry(new Point(12, 12) + direction * 1.2, new Point(12, 12) + direction * length));
        }
        group.Freeze();
        return group;
    });

    // OpenAI's knot as published by Simple Icons (simpleicons.org, "OpenAI", on a 24 × 24 grid), filled.
    const string KnotPath =
        "F1 M22.2819 9.8211a5.9847 5.9847 0 0 0-.5157-4.9108 6.0462 6.0462 0 0 0-6.5098-2.9A6.0651 6.0651 0 0 0 4.9807 4.1818"
        + "a5.9847 5.9847 0 0 0-3.9977 2.9 6.0462 6.0462 0 0 0 .7427 7.0966 5.98 5.98 0 0 0 .511 4.9107 6.051 6.051 0 0 0 6.5146 2.9001"
        + "A5.9847 5.9847 0 0 0 13.2599 24a6.0557 6.0557 0 0 0 5.7718-4.2058 5.9894 5.9894 0 0 0 3.9977-2.9001 6.0557 6.0557 0 0 0-.7475-7.0729z"
        + "m-9.022 12.6081a4.4755 4.4755 0 0 1-2.8764-1.0408l.1419-.0804 4.7783-2.7582a.7948.7948 0 0 0 .3927-.6813v-6.7369l2.02 1.1686"
        + "a.071.071 0 0 1 .038.052v5.5826a4.504 4.504 0 0 1-4.4945 4.4944z"
        + "m-9.6607-4.1254a4.4708 4.4708 0 0 1-.5346-3.0137l.142.0852 4.783 2.7582a.7712.7712 0 0 0 .7806 0l5.8428-3.3685v2.3324"
        + "a.0804.0804 0 0 1-.0332.0615L9.74 19.9502a4.4992 4.4992 0 0 1-6.1408-1.6464z"
        + "M2.3408 7.8956a4.485 4.485 0 0 1 2.3655-1.9728V11.6a.7664.7664 0 0 0 .3879.6765l5.8144 3.3543-2.0201 1.1685"
        + "a.0757.0757 0 0 1-.071 0l-4.8303-2.7865A4.504 4.504 0 0 1 2.3408 7.872z"
        + "m16.5963 3.8558L13.1038 8.364 15.1192 7.2a.0757.0757 0 0 1 .071 0l4.8303 2.7913a4.4944 4.4944 0 0 1-.6765 8.1042v-5.6772"
        + "a.79.79 0 0 0-.407-.667z"
        + "m2.0107-3.0231l-.142-.0852-4.7735-2.7818a.7759.7759 0 0 0-.7854 0L9.409 9.2297V6.8974a.0662.0662 0 0 1 .0284-.0615"
        + "l4.8303-2.7866a4.4992 4.4992 0 0 1 6.6802 4.66z"
        + "M8.3065 12.863l-2.02-1.1638a.0804.0804 0 0 1-.038-.0567V6.0742a4.4992 4.4992 0 0 1 7.3757-3.4537l-.142.0805L8.704 5.459"
        + "a.7948.7948 0 0 0-.3927.6813z"
        + "m1.0976-2.3654l2.602-1.4998 2.6069 1.4998v2.9994l-2.5974 1.4997-2.6067-1.4997Z";

    static readonly Lazy<Geometry> KnotGeometry = new(() =>
    {
        var geometry = Geometry.Parse(KnotPath);
        geometry.Freeze();
        return geometry;
    });

    static FrameworkElement Knot(Brush fill) => new Canvas { Width = 24, Height = 24, Children = { new Path { Data = KnotGeometry.Value, Fill = fill } } };

    /// <summary>The mark at <paramref name="size"/> pixels, in <paramref name="brush"/> or its own colour.</summary>
    public static FrameworkElement For(AiTool tool, double size, Brush? brush = null)
    {
        bool claude = tool == AiTool.ClaudeCode;
        if (!claude) return new Viewbox { Width = size, Height = size, Child = Knot(brush ?? Brushes.White) };
        var mark = new Path
        {
            Data = Spark.Value,
            Stroke = brush ?? new SolidColorBrush(ClaudeColor),
            StrokeThickness = 2.35,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        return new Viewbox { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { mark } } };
    }
}
