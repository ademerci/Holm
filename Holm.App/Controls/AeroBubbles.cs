using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Holm.App;

/// <summary>Glossy bubbles painted over an opaque Aero surface.</summary>
sealed class AeroBubbles : Control
{
    static readonly (double X, double Y, double Radius)[] Bubbles =
    {
        (0.76, 0.72, 31), (0.43, 0.85, 19), (0.19, 0.98, 10), (0.82, 0.93, 14),
    };
    static readonly IBrush Fill = Look.VerticalGradient((0x78FFFFFF, 0), (0x14FFFFFF, 0.54), (0x6243BBF0, 1));
    static readonly IPen Edge = new Pen(Look.Brush(0x9EFFFFFF), 1);
    static readonly IBrush Highlight = Look.Brush(0x90FFFFFF);

    public AeroBubbles() => IsHitTestVisible = false;

    public override void Render(DrawingContext context)
    {
        foreach (var (x, y, radius) in Bubbles)
        {
            double r = Math.Min(radius, Math.Min(Bounds.Width, Bounds.Height) / 9);
            if (r < 2) continue;
            var center = new Point(r + x * (Bounds.Width - 2 * r), r + y * (Bounds.Height - 2 * r));
            context.DrawEllipse(Fill, Edge, center, r, r);
            context.DrawEllipse(Highlight, null, new Point(center.X - r * 0.25, center.Y - r * 0.57), r * 0.35, r * 0.13);
        }
    }
}
