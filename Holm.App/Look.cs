using Avalonia.Media;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Which design Holm runs in (Settings › Design, <see cref="Designs"/>), read once at start; switching restarts Holm.
/// Its layout is the classic one or the instrument one (black, white ink, small corners, tools as words); its
/// colours, corners and type are the design's.
/// </summary>
internal static class Look
{
    public static Design Current { get; set; } = Designs.Current(AppSettings.Load());

    public static bool Instrument => Current.Instrument;
    public static bool Aero => Current.Id == "aero";

    /// <summary>The instrument value or the classic one.</summary>
    public static T Pick<T>(T instrument, T classic) => Instrument ? instrument : classic;

    public static IBrush Brush(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();

    public static IBrush VerticalGradient(params (uint Color, double Offset)[] colors)
    {
        var stops = new GradientStops();
        foreach (var (color, offset) in colors) stops.Add(new GradientStop(Color.FromUInt32(color), offset));
        return new LinearGradientBrush
        {
            StartPoint = new(0, 0, Avalonia.RelativeUnit.Relative), EndPoint = new(0, 1, Avalonia.RelativeUnit.Relative),
            GradientStops = stops,
        }.ToImmutable();
    }

    /// <summary>The design's ink (text and icons), and the same ink faded to <paramref name="alpha"/>.</summary>
    public static readonly IBrush Ink = Brush(Current.Ink);
    public static IBrush InkA(byte alpha) => Brush(Designs.WithAlpha(Current.Ink, alpha));

    public static readonly IBrush Accent = Brush(Current.Accent);
    public static readonly IBrush OnAccent = Brush(Designs.OnColor(Current.Accent));

    /// <summary>The design's type for text: Inter, or a system monospace or serif face.</summary>
    public static readonly FontFamily Font = Current.Font switch
    {
        "mono" => new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, Liberation Mono, monospace"),
        "serif" => new FontFamily("Georgia, Cambria, DejaVu Serif, Liberation Serif, serif"),
        _ => Fonts.Text,
    };

    /// <summary>Headings as the design writes them (capitals in some).</summary>
    public static string Heading(string text) => Current.Caps ? text.ToUpperInvariant() : text;

    /// <summary>The notch's fill: one colour, or a gradient across it.</summary>
    public static IBrush NotchFill(Design? design = null)
    {
        design ??= Current;
        var colors = design.Fill;
        if (design.Id == "aero")
            return VerticalGradient((colors[0], 0), (colors[1], 0.32), (colors[2], 0.67), (colors[3], 1));
        if (colors.Length == 1) return Brush(colors[0]);
        var stops = new GradientStops();
        for (int i = 0; i < colors.Length; i++) stops.Add(new GradientStop(Color.FromUInt32(colors[i]), i / (double)(colors.Length - 1)));
        var rel = Avalonia.RelativeUnit.Relative;
        return design.FillKind switch
        {
            "conic" => new ConicGradientBrush { GradientStops = stops, Center = new Avalonia.RelativePoint(0.5, 0.15, rel), Angle = 200 }.ToImmutable(),
            "diagonal" => new LinearGradientBrush { GradientStops = stops, StartPoint = new(0, 0, rel), EndPoint = new(1, 1, rel) }.ToImmutable(),
            _ => new LinearGradientBrush { GradientStops = stops, StartPoint = new(0, 0, rel), EndPoint = new(0, 1, rel) }.ToImmutable(),
        };
    }
}
