using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WinNotch;
using Design = WinNotch.Design;

namespace Holm.App.Controls;

/// <summary>Surface details shared by the live notch, the sidebar and the preview in the design picker.</summary>
internal sealed class DesignDetail(Design design, bool preview) : Control
{
    static IBrush? _blueprintGrid;
    readonly Pen _mark = new(Look.Brush(Designs.WithAlpha(design.Accent, 0x90)), 1);

    public static void AddTo(Panel panel, Design design, bool preview = false)
    {
        if (design.Id is "blueprint" or "marginnotes" or "commanddeck")
            panel.Children.Add(new DesignDetail(design, preview) { IsHitTestVisible = false });
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (bounds.Width < 20 || bounds.Height < 12) return;
        if (design.Id == "blueprint")
        {
            context.DrawRectangle(_blueprintGrid ??= MakeGrid(design.Ink), null, bounds);
            return;
        }

        double inset = preview ? 5.5 : 14.5;
        if (design.Id == "marginnotes")
        {
            context.DrawLine(_mark, new Point(inset, 0), new Point(inset, bounds.Height));
            context.DrawLine(_mark, new Point(inset + 3, 0), new Point(inset + 3, bounds.Height));
            return;
        }

        // A small prompt and its steady block cursor sit in the top margin of the command surface.
        double x = bounds.Right - inset - 15;
        context.DrawLine(_mark, new Point(x, 4), new Point(x + 3, 7));
        context.DrawLine(_mark, new Point(x + 3, 7), new Point(x, 10));
        context.DrawRectangle(_mark.Brush, null, new Rect(x + 6, 8, 6, 2));
    }

    // A tile of drafting paper with four subdivisions, repeated at its original scale.
    static IBrush MakeGrid(uint ink)
    {
        const int pitch = 32;
        var major = new Pen(Look.Brush(Designs.WithAlpha(ink, 0x25)), 1);
        var minor = new Pen(Look.Brush(Designs.WithAlpha(ink, 0x0E)), 1);
        var tile = new DrawingGroup();
        using (var drawing = tile.Open())
        {
            drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, pitch, pitch));
            for (int i = 0; i < pitch; i += 8)
            {
                var pen = i == 0 ? major : minor;
                drawing.DrawLine(pen, new Point(i + 0.5, 0), new Point(i + 0.5, pitch));
                drawing.DrawLine(pen, new Point(0, i + 0.5), new Point(pitch, i + 0.5));
            }
        }
        return new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, pitch, pitch, RelativeUnit.Absolute),
        };
    }
}
