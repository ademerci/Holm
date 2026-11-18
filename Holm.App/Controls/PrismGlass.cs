using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WinNotch;

namespace Holm.App.Controls;

/// <summary>The optical rim over a translucent surface. The notch supplies its live spring outline.</summary>
internal sealed class PrismGlass(double corners = 0) : Control
{
    internal const string DesignId = "liquid"; // Also the saved ID used by the original design.

    // A dark inner bevel separates the broad, dispersed reflection from the sharp surface glint.
    static readonly IBrush InnerShade = Look.VerticalGradient((0x42090D18, 0), (0x08090D18, 0.38), (0x60090D18, 1));
    static readonly IBrush Dispersion = Diagonal((0x885DAEFF, 0), (0x40CBDFFF, 0.16),
        (0x00CBDFFF, 0.45), (0x30B5B5FF, 0.73), (0x80E8BDFF, 1));
    static readonly Pen Reflection = new(Diagonal((0xF0FFFFFF, 0), (0x8AEBF7FF, 0.22),
        (0x26FFFFFF, 0.48), (0x30FFFFFF, 0.65), (0xD4F5EEFF, 1)), 2);
    static readonly Pen RoundedReflection = new(Reflection.Brush, 1.15);

    Geometry? _outline;
    double _depth;

    public void ApplyOutline(Geometry outline, double height)
    {
        _outline = outline;
        _depth = Math.Clamp(height * 0.065, 2, 8);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 2 || bounds.Height < 2) return;

        if (_outline is { } outline)
        {
            // The backdrop clips the outside half so the bevel follows the shoulder and corner arcs.
            context.DrawGeometry(null, new Pen(InnerShade, _depth * 2), outline);
            context.DrawGeometry(null, new Pen(Dispersion, _depth), outline);
            context.DrawGeometry(null, Reflection, outline);
            return;
        }

        // On the hub and its sidebar the rim sits just inside the rounded surface.
        context.DrawRectangle(null, new Pen(InnerShade, 4), bounds.Deflate(2), Math.Max(0, corners - 2), Math.Max(0, corners - 2));
        context.DrawRectangle(null, RoundedReflection, bounds.Deflate(0.6), Math.Max(0, corners - 0.6), Math.Max(0, corners - 0.6));
    }

    public static IBrush NotchSurface(WinNotch.Design design)
    {
        var colors = design.Fill;
        return Look.VerticalGradient((colors[0], 0), (colors[1], 0.20), (colors[1], 0.70), (colors[2], 1));
    }

    public static IBrush FloatingSurface(uint tint, uint ink) => Look.VerticalGradient(
        (Designs.WithAlpha(ink, 0x28), 0), (tint, 0.28), (tint, 0.70), (Designs.WithAlpha(ink, 0x16), 1));

    static IBrush Diagonal(params (uint Color, double Offset)[] colors)
    {
        var stops = new GradientStops();
        foreach (var (color, offset) in colors) stops.Add(new GradientStop(Color.FromUInt32(color), offset));
        return new LinearGradientBrush
        {
            StartPoint = new(0, 0, RelativeUnit.Relative), EndPoint = new(1, 1, RelativeUnit.Relative),
            GradientStops = stops,
        }.ToImmutable();
    }
}
