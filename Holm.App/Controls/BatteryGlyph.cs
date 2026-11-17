using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Holm.App.Controls;

/// <summary>
/// iOS-style battery: outline, proportional fill, green while plugged in, red when low. Drawn directly, with the
/// same coordinates as the WPF app's BatteryGlyph (Controls.cs).
/// </summary>
public sealed class BatteryGlyph : Control
{
    static readonly IBrush Outline = new SolidColorBrush(Color.FromArgb(0x73, 0xFF, 0xFF, 0xFF)).ToImmutable();
    static readonly IPen OutlinePen = new Pen(Outline, 1).ToImmutable();
    static readonly IBrush Green = new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)).ToImmutable();
    static readonly IBrush Red = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)).ToImmutable();
    static readonly IPen BoltPen = new Pen(Brushes.Black, 0.9, lineJoin: PenLineJoin.Round).ToImmutable();
    static readonly Geometry Bolt = Geometry.Parse("M12.6,1.6 L8.4,6.6 L11,6.6 L9.6,10.4 L13.8,5.4 L11.2,5.4 Z");

    double _level = 100;
    bool _charging;

    /// <summary>0 to 100.</summary>
    public double Level
    {
        get => _level;
        set { if (_level != value) { _level = value; InvalidateVisual(); } }
    }

    public bool Charging
    {
        get => _charging;
        set { if (_charging != value) { _charging = value; InvalidateVisual(); } }
    }

    protected override Size MeasureOverride(Size availableSize) => new(25, 12);

    public override void Render(DrawingContext dc)
    {
        dc.DrawRectangle(null, OutlinePen, new Rect(0.5, 0.5, 21, 11), 3.2, 3.2);
        dc.DrawRectangle(Outline, null, new Rect(22.6, 4, 1.8, 4), 0.9, 0.9);

        double level = Math.Clamp(Level, 0, 100);
        var fill = Charging ? Green : level <= 20 ? Red : Brushes.White;
        dc.DrawRectangle(fill, null, new Rect(2, 2, Math.Max(2, 18 * level / 100), 8), 1.6, 1.6);

        if (Charging) dc.DrawGeometry(Brushes.White, BoltPen, Bolt);
    }
}
