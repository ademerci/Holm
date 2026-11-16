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

    // OpenAI's knot: six links, each rounded on the outside and square on the inside, turned by 60° around a hexagonal
    // opening and woven so each one runs over the next. Measured on a 24 × 24 grid, outward is up.
    internal static double KnotWidth = 9.4, KnotOffset = -1.95, KnotInner = 1.62, KnotReach = 10.9, KnotSplit = -1.2;

    static Geometry Link(int turn)
    {
        double half = KnotWidth / 2, centre = KnotReach - half;
        var link = new StreamGeometry();
        using (var c = link.Open())
        {
            c.BeginFigure(new Point(KnotOffset - half, KnotInner), true, true);
            c.LineTo(new Point(KnotOffset - half, -centre), true, true);
            c.ArcTo(new Point(KnotOffset + half, -centre), new Size(half, half), 0, false, SweepDirection.Clockwise, true, true);
            c.LineTo(new Point(KnotOffset + half, KnotInner), true, true);
        }
        var place = new TransformGroup();
        place.Children.Add(new RotateTransform(turn * 60));
        place.Children.Add(new TranslateTransform(12, 12));
        link.Transform = place;
        link.Freeze();
        return link;
    }

    /// <summary>
    /// The knot as one outline: the lobes' outer edge, and each link's inner edge, which forms a side of the hexagon and
    /// runs out into its lobe, the swirl of the original. Weaving every crossing doesn't survive 12 px; this does.
    /// </summary>
    static readonly Lazy<(Geometry Outline, Geometry Swirl)> KnotParts = new(() =>
    {
        Geometry outline = Link(0);
        for (int i = 1; i < 6; i++) outline = Geometry.Combine(outline, Link(i), GeometryCombineMode.Union, null);
        outline.Freeze();
        // Each inner edge from its corner of the hexagon out to the edge of the lobes (the outline clips it there).
        var swirl = new GeometryGroup();
        double edge = KnotOffset + KnotWidth / 2;
        for (int i = 0; i < 6; i++)
        {
            var place = new TransformGroup();
            place.Children.Add(new RotateTransform(i * 60));
            place.Children.Add(new TranslateTransform(12, 12));
            swirl.Children.Add(new LineGeometry(new Point(edge, -KnotReach - 2), new Point(edge, KnotInner), place));
        }
        swirl.Freeze();
        return (outline, swirl);
    });

    static FrameworkElement Knot(Brush stroke)
    {
        var (outline, swirl) = KnotParts.Value;
        Path Line(Geometry data) => new() { Data = data, Stroke = stroke, StrokeThickness = 1.45, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var inner = Line(swirl);
        inner.Clip = outline;
        return new Canvas { Width = 24, Height = 24, Children = { Line(outline), inner } };
    }

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
