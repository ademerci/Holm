using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace WinNotch;

/// <summary>QR codes as vector shapes, so they stay sharp at any notch size.</summary>
internal static class QrCode
{
    /// <summary>The dark modules of the code for <paramref name="text"/>, one unit per module, without the quiet zone.</summary>
    public static Geometry For(string text, out int modules)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix; // with a quiet zone of 4 modules on every side
        const int quiet = 4;
        modules = matrix.Count - 2 * quiet;

        var geometry = new StreamGeometry();
        using (var c = geometry.Open())
        {
            for (int y = 0; y < modules; y++)
            {
                var row = matrix[y + quiet];
                for (int x = 0; x < modules; x++)
                {
                    if (!row[x + quiet]) continue;
                    int start = x;
                    while (x + 1 < modules && row[x + 1 + quiet]) x++; // one rectangle per run of dark modules
                    c.BeginFigure(new Point(start, y), true, true);
                    c.PolyLineTo(new[] { new Point(x + 1, y), new Point(x + 1, y + 1), new Point(start, y + 1) }, false, false);
                }
            }
        }
        geometry.Freeze();
        return geometry;
    }

    /// <summary>The code drawn black on white, filling <paramref name="size"/> pixels, quiet zone included.</summary>
    public static FrameworkElement Image(string text, double size)
    {
        var geometry = For(text, out int modules);
        double module = size / (modules + 8);
        var path = new System.Windows.Shapes.Path
        {
            Data = geometry, Fill = Brushes.Black, Stretch = Stretch.Fill,
            Width = module * modules, Height = module * modules, SnapsToDevicePixels = true,
        };
        RenderOptions.SetEdgeMode(path, EdgeMode.Aliased); // no seams between neighbouring modules
        return new System.Windows.Controls.Border
        {
            Width = size, Height = size, Background = Brushes.White, CornerRadius = new CornerRadius(module * 2),
            Child = path,
        };
    }
}
