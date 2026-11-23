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

    // Light crosses the upper-left lip and collects on the opposite edge. Keep the face clear between them.
    static readonly IBrush InnerShade = Diagonal((0x22505C70, 0), (0x02505C70, 0.27), (0x00505C70, 0.64), (0x36505C70, 1));
    static readonly IBrush Dispersion = Diagonal((0xAFBAECFF, 0), (0x107BC7E4, 0.14), (0x007BC7E4, 0.32),
        (0x00BBA6E1, 0.73), (0x78ACA8D0, 0.88), (0xEAFFF3E5, 1));
    static readonly Pen Reflection = new(Diagonal((0xFFFFFFFF, 0), (0xDBFFFFFF, 0.19), (0x245D7082, 0.42),
        (0x0A4E5869, 0.64), (0xCBDCE8F2, 0.82), (0xFFFFFFFF, 1)), 1.6);
    static readonly Pen RoundedReflection = new(Reflection.Brush, 1);
    static readonly Pen RoundedBevel = new(InnerShade, 3), RoundedFringe = new(Dispersion, 1.8);
    static readonly IBrush LensFace = Look.VerticalGradient((0xBFFFFFFF, 0), (0x1CFFFFFF, 0.28),
        (0x04FFFFFF, 0.52), (0x18556675, 0.80), (0xAFFFFFFF, 1));
    static readonly BoxShadows LensShadow = new(new BoxShadow { OffsetY = 1, Blur = 2, Color = Color.FromUInt32(0x50536776) });

    // Only the spring outline receives pointer light. Rounded panels and preview tiles use the static reflections.
    LinearGradientBrush? _light;
    Pen? _lightPen;

    Geometry? _outline;
    readonly Pen _bevel = new(InnerShade), _innerLip = new(InnerShade), _fringe = new(Dispersion);

    public void ApplyOutline(Geometry outline, double height)
    {
        _outline = outline;
        double depth = Math.Clamp(height * 0.05, 2, 6);
        _bevel.Thickness = depth * 2;
        _innerLip.Thickness = depth * 1.3;
        _fringe.Thickness = depth * 0.75;
        InvalidateVisual();
    }

    public void AimLight(double x)
    {
        if (Bounds.Width <= 0) return;
        double center = Math.Clamp(x / Bounds.Width, 0, 1);
        _light ??= new LinearGradientBrush { GradientStops =
        {
            new GradientStop(Colors.Transparent, 0), new GradientStop(Colors.White, 0.5), new GradientStop(Colors.Transparent, 1),
        } };
        _light.StartPoint = new RelativePoint(center - 0.18, 0.38, RelativeUnit.Relative);
        _light.EndPoint = new RelativePoint(center + 0.18, 0.62, RelativeUnit.Relative);
        _lightPen ??= new Pen(_light, 2.5);
        InvalidateVisual();
    }

    public void ClearLight()
    {
        _lightPen = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 2 || bounds.Height < 2) return;

        if (_outline is { } outline)
        {
            // The backdrop clips the outside half so the bevel follows the shoulder and corner arcs.
            context.DrawGeometry(null, _bevel, outline);
            context.DrawGeometry(null, _innerLip, outline);
            context.DrawGeometry(null, _fringe, outline);
            context.DrawGeometry(null, Reflection, outline);
            if (_lightPen != null) context.DrawGeometry(null, _lightPen, outline);
            return;
        }

        // On the hub and its sidebar the rim sits just inside the rounded surface.
        context.DrawRectangle(null, RoundedBevel, bounds.Deflate(1.5), Math.Max(0, corners - 1.5), Math.Max(0, corners - 1.5));
        context.DrawRectangle(null, RoundedFringe, bounds.Deflate(1), Math.Max(0, corners - 1), Math.Max(0, corners - 1));
        context.DrawRectangle(null, RoundedReflection, bounds.Deflate(0.6), Math.Max(0, corners - 0.6), Math.Max(0, corners - 0.6));
    }

    public static IBrush NotchSurface(WinNotch.Design design)
    {
        var colors = design.Fill;
        return Look.VerticalGradient((colors[0], 0), (colors[1], 0.20), (colors[1], 0.70), (colors[2], 1));
    }

    public static IBrush FloatingSurface(uint tint, uint ink) => Look.VerticalGradient(
        (0x9CFBFDFF, 0), (tint, 0.10), (tint, 0.88), (Designs.WithAlpha(ink, 0x0C), 0.985), (0x7AFFFFFF, 1));

    // The coloured track is drawn again, magnified inside the rounded clip. Its edge remains visible through the thumb.
    public static void DrawLens(DrawingContext context, Rect lens, Rect track, IBrush color)
    {
        double radius = lens.Height / 2;
        var shape = new RoundedRect(lens, radius);
        context.DrawRectangle(Brushes.Transparent, null, shape, LensShadow);
        using (context.PushClip(shape))
        {
            var center = lens.Center;
            var magnified = new Rect(center.X + (track.X - center.X) * 1.25, center.Y + (track.Y - center.Y) * 1.65,
                track.Width * 1.25, track.Height * 1.65);
            context.DrawRectangle(color, null, magnified, magnified.Height / 2, magnified.Height / 2);
        }
        context.DrawRectangle(LensFace, RoundedReflection, shape);
    }

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
