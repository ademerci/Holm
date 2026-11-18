using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Holm.App;

/// <summary>Sunlight, sweeping green hills and glass bubbles painted over the Aero sky.</summary>
sealed class AeroScenery : Control
{
    static readonly (double X, double Y, double Radius)[] NotchBubbles =
    {
        (0.75, 0.37, 29), (0.92, 0.44, 12), (0.65, 0.46, 7), (0.94, 0.69, 6),
    };
    static readonly (double X, double Y, double Radius)[] SidebarBubbles =
    {
        (0.76, 0.70, 36), (0.89, 0.56, 14), (0.44, 0.62, 9), (0.25, 0.83, 17), (0.65, 0.55, 5),
    };
    static readonly Geometry FarHill = Geometry.Parse("M0,88 C28,96 58,91 77,76 C87,68 95,64 100,66 L100,100 L0,100 Z");
    static readonly Geometry NearHill = Geometry.Parse("M0,82 C23,83 26,99 56,94 C76,92 90,79 100,80 L100,100 L0,100 Z");
    static readonly IBrush FarGrass = Look.VerticalGradient((0xFFC4F878, 0), (0xFF8FDB4B, 0.36), (0xFF4EBC3C, 1));
    static readonly IBrush NearGrass = Look.VerticalGradient((0xFFE5FF91, 0), (0xFFA4E64C, 0.26), (0xFF55BE3B, 1));
    static readonly IBrush Sunlight = Radial((0xFFFFFFFF, 0), (0xB9F9FFFF, 0.25), (0x00F9FFFF, 1));
    static readonly IBrush BubbleGlass = Radial((0x08FFFFFF, 0.37), (0x3A81F3FF, 0.73), (0xC01D8ACC, 0.93), (0xE0B6F9FF, 1));
    static readonly IPen Rim = new Pen(Look.Brush(0xB8FFFFFF), 0.9);
    static readonly IPen Refraction = new Pen(Look.Brush(0x6545AEE8), 1);
    static readonly IBrush Glint = Look.VerticalGradient((0xF4FFFFFF, 0), (0x22FFFFFF, 1));
    static readonly IBrush ReflectedLight = Look.Brush(0x8CD6FFFF);

    readonly bool _sidebar;

    public AeroScenery(bool sidebar = false)
    {
        _sidebar = sidebar;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        double width = Bounds.Width, height = Bounds.Height;
        // The narrow, closed notch keeps the sky gradient while leaving its clock and icons clear.
        if (width <= 0 || height <= 0) return;
        context.DrawEllipse(Sunlight, null, new Point(width * 0.88, height * 0.07), width * 0.31, height * 0.54);
        double detailOpacity = _sidebar ? 1 : Math.Clamp((height - 44) / 42, 0, 1);
        if (detailOpacity == 0) return;
        using var detail = context.PushOpacity(detailOpacity);

        using (context.PushTransform(Matrix.CreateScale(width / 100, height / 100)))
        {
            context.DrawGeometry(FarGrass, null, FarHill);
            context.DrawGeometry(NearGrass, null, NearHill);
        }

        foreach (var (x, y, radius) in _sidebar ? SidebarBubbles : NotchBubbles)
        {
            double r = Math.Min(radius, Math.Min(width, height) * 0.17);
            var center = new Point(width * x, height * y);
            context.DrawEllipse(null, Refraction, center, r + 0.8, r + 0.8);
            context.DrawEllipse(BubbleGlass, Rim, center, r, r);
            context.DrawEllipse(Glint, null, new Point(center.X - r * 0.24, center.Y - r * 0.54), r * 0.39, r * 0.18);
            context.DrawEllipse(ReflectedLight, null, new Point(center.X + r * 0.30, center.Y + r * 0.62), r * 0.25, r * 0.09);
        }
    }

    static IBrush Radial(params (uint Color, double Offset)[] colors)
    {
        var stops = new GradientStops();
        foreach (var (color, offset) in colors)
            stops.Add(new GradientStop(Color.FromUInt32(color), offset));
        return new RadialGradientBrush { GradientStops = stops }.ToImmutable();
    }
}
