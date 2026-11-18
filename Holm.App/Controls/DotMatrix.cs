using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Holm.App.Controls;

/// <summary>
/// The dot-matrix look of the Dot Matrix design: a 5×7 dot font and a fine grid of dots for surfaces.
/// </summary>
internal static class DotMatrix
{
    public const string DesignId = "dotmatrix";
    public const int Rows = 7;

    /// <summary>Each glyph as seven rows of five columns, '#' a lit dot.</summary>
    static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." },
        ['1'] = new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['2'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" },
        ['3'] = new[] { "#####", "...#.", "..#..", "...#.", "....#", "#...#", ".###." },
        ['4'] = new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." },
        ['5'] = new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." },
        ['6'] = new[] { "..##.", ".#...", "#....", "####.", "#...#", "#...#", ".###." },
        ['7'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." },
        ['8'] = new[] { ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###." },
        ['9'] = new[] { ".###.", "#...#", "#...#", ".####", "....#", "...#.", ".##.." },
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['B'] = new[] { "####.", "#...#", "#...#", "####.", "#...#", "#...#", "####." },
        ['C'] = new[] { ".###.", "#...#", "#....", "#....", "#....", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['F'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#...." },
        ['G'] = new[] { ".###.", "#...#", "#....", "#.###", "#...#", "#...#", ".####" },
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['I'] = new[] { ".###.", "..#..", "..#..", "..#..", "..#..", "..#..", ".###." },
        ['J'] = new[] { "..###", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##.." },
        ['K'] = new[] { "#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['P'] = new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." },
        ['Q'] = new[] { ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#" },
        ['R'] = new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" },
        ['S'] = new[] { ".####", "#....", "#....", ".###.", "....#", "....#", "####." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['V'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#.." },
        ['W'] = new[] { "#...#", "#...#", "#...#", "#.#.#", "#.#.#", "#.#.#", ".#.#." },
        ['X'] = new[] { "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#" },
        ['Y'] = new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." },
        ['Z'] = new[] { "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####" },
        [':'] = new[] { ".....", ".....", "..#..", ".....", "..#..", ".....", "....." },
        ['.'] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "..#.." },
        [','] = new[] { ".....", ".....", ".....", ".....", ".....", "..#..", ".#..." },
        ['-'] = new[] { ".....", ".....", ".....", ".###.", ".....", ".....", "....." },
        ['+'] = new[] { ".....", "..#..", "..#..", "#####", "..#..", "..#..", "....." },
        ['/'] = new[] { "....#", "....#", "...#.", "..#..", ".#...", "#....", "#...." },
        ['%'] = new[] { "##..#", "##..#", "...#.", "..#..", ".#...", "#..##", "#..##" },
        ['°'] = new[] { ".#...", "#.#..", ".#...", ".....", ".....", ".....", "....." },
        ['!'] = new[] { "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#.." },
        ['?'] = new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#.." },
        ['\''] = new[] { "..#..", "..#..", ".....", ".....", ".....", ".....", "....." },
        ['('] = new[] { "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#." },
        [')'] = new[] { ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#..." },
        ['·'] = new[] { ".....", ".....", ".....", "..#..", ".....", ".....", "....." },
    };

    /// <summary>A glyph's lit columns, trimmed of empty ones except for digits (so a clock doesn't jitter).</summary>
    public readonly record struct Glyph(string[] Rows, int First, int Width);

    static readonly Dictionary<char, Glyph> Cache = new();

    /// <summary>The glyphs for <paramref name="text"/>, or false when one of its letters has no dots.</summary>
    public static bool TryLayout(string text, List<Glyph?> glyphs)
    {
        glyphs.Clear();
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw) switch { '–' or '—' => '-', '•' => '·', ' ' or ' ' or ' ' => ' ', var u => u };
            if (c == ' ') { glyphs.Add(null); continue; }
            if (!Cache.TryGetValue(c, out var glyph))
            {
                if (!Glyphs.TryGetValue(c, out var rows)) return false;
                int first = 0, last = 4;
                if (!char.IsDigit(c))
                {
                    while (first < 4 && Empty(rows, first)) first++;
                    while (last > first && Empty(rows, last)) last--;
                }
                Cache[c] = glyph = new Glyph(rows, first, last - first + 1);
            }
            glyphs.Add(glyph);
        }
        return true;
    }

    static bool Empty(string[] rows, int column)
    {
        foreach (string row in rows) if (row[column] == '#') return false;
        return true;
    }

    /// <summary>Columns a laid-out text takes: glyphs, a space of three, one between each.</summary>
    public static int Columns(List<Glyph?> glyphs)
    {
        int columns = 0;
        for (int i = 0; i < glyphs.Count; i++) columns += (glyphs[i]?.Width ?? 3) + (i > 0 ? 1 : 0);
        return columns;
    }

    /// <summary>A fine grid of dots, tiled, as on the back of a phone with a dot display.</summary>
    public static IBrush Grid(uint ink, byte alpha = 0x1A, double pitch = 6, double dot = 1.1)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, pitch, pitch)) });
        double at = (pitch - dot) / 2;
        group.Children.Add(new GeometryDrawing
        {
            Brush = new SolidColorBrush(Color.FromUInt32((uint)alpha << 24 | ink & 0xFFFFFF)),
            Geometry = new EllipseGeometry(new Rect(at, at, dot, dot)),
        });
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, pitch, pitch, RelativeUnit.Absolute),
        };
    }
}

/// <summary>
/// A <see cref="TextBlock"/> that writes in dots when the Dot Matrix design runs (and its text has dots for every
/// letter), and as plain text otherwise. Size, colour and opacity come from the TextBlock as usual.
/// </summary>
internal sealed class DotTextBlock : TextBlock
{
    readonly List<DotMatrix.Glyph?> _glyphs = new();
    bool _dots;

    /// <summary>Dots only in the Dot Matrix design.</summary>
    public static bool Enabled => Look.Current.Id == DotMatrix.DesignId;

    /// <summary>The distance between two dots: the font size's capital height over seven rows, never too fine to read.</summary>
    double Pitch => System.Math.Max(FontSize * 0.74 / DotMatrix.Rows, 1.8);

    protected override Size MeasureOverride(Size availableSize)
    {
        _dots = Enabled && !string.IsNullOrEmpty(Text) && DotMatrix.TryLayout(Text!, _glyphs);
        if (!_dots) return base.MeasureOverride(availableSize);
        double pitch = Pitch;
        var padding = Padding;
        return new Size(DotMatrix.Columns(_glyphs) * pitch + padding.Left + padding.Right,
            (DotMatrix.Rows + 2) * pitch + padding.Top + padding.Bottom);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (!_dots) { base.RenderTextLayout(context, origin); return; }
        var ink = Foreground;
        if (ink == null) return;
        double pitch = Pitch, radius = pitch * 0.42;
        var padding = Padding;
        double height = Bounds.Height - padding.Top - padding.Bottom;
        double x = padding.Left + pitch / 2, y = padding.Top + (height - DotMatrix.Rows * pitch) / 2 + pitch / 2;
        foreach (var glyph in _glyphs)
        {
            if (glyph is not { } g) { x += 4 * pitch; continue; }
            for (int row = 0; row < DotMatrix.Rows; row++)
                for (int col = 0; col < g.Width; col++)
                    if (g.Rows[row][g.First + col] == '#')
                        context.DrawEllipse(ink, null, new Point(x + col * pitch, y + row * pitch), radius, radius);
            x += (g.Width + 1) * pitch;
        }
    }
}
