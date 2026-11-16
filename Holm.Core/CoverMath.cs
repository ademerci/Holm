using System;

namespace WinNotch;

/// <summary>
/// The pixel maths behind a cover's backdrop and accent colour, on plain BGRA bytes: the same numbers whichever
/// toolkit decoded the picture and draws the result.
/// </summary>
internal static class CoverMath
{
    /// <summary>The backdrop is this many pixels wide and high.</summary>
    public const int BackdropSize = 24;

    /// <summary>
    /// Turns a 24×24 BGRA copy of the cover, in place, into a heavily blurred, slightly more saturated one. Stretched
    /// across the notch it gives the soft colour wash of the iPhone player. Blurring it once here is far cheaper than
    /// a blur effect, which would be recomputed on every animation frame. Returns the average luminance (0 to 1).
    /// </summary>
    public static double Backdrop(byte[] px)
    {
        const int n = BackdropSize;
        var planes = new double[3][]; // B, G, R
        for (int c = 0; c < 3; c++)
        {
            planes[c] = new double[n * n];
            for (int i = 0; i < n * n; i++) planes[c][i] = px[i * 4 + c] / 255.0;
        }
        for (int pass = 0; pass < 3; pass++) // three box blurs ≈ one gaussian
            foreach (var plane in planes) { BoxBlur(plane, n, horizontal: true); BoxBlur(plane, n, horizontal: false); }

        double luminance = 0;
        for (int i = 0; i < n * n; i++)
        {
            double b = planes[0][i], g = planes[1][i], r = planes[2][i];
            double gray = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            luminance += gray;
            px[i * 4] = ToByte(gray + (b - gray) * 1.35);
            px[i * 4 + 1] = ToByte(gray + (g - gray) * 1.35);
            px[i * 4 + 2] = ToByte(gray + (r - gray) * 1.35);
            px[i * 4 + 3] = 255;
        }
        return luminance / (n * n);
    }

    /// <summary>How much black goes over a backdrop of this luminance so white text stays readable, even on a white cover.</summary>
    public static double ShadeFor(double luminance) => Math.Clamp(1 - 0.22 / Math.Max(luminance, 0.01), 0.25, 0.8);

    static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);

    static void BoxBlur(double[] plane, int n, bool horizontal, int radius = 2)
    {
        var line = new double[n];
        for (int a = 0; a < n; a++)
        {
            for (int b = 0; b < n; b++) line[b] = plane[horizontal ? a * n + b : b * n + a];
            for (int b = 0; b < n; b++)
            {
                double sum = 0;
                for (int k = -radius; k <= radius; k++) sum += line[Math.Clamp(b + k, 0, n - 1)];
                plane[horizontal ? a * n + b : b * n + a] = sum / (2 * radius + 1);
            }
        }
    }

    /// <summary>A bright, saturated colour from a small BGRA copy of the cover, used for the equalizer and the progress bar.</summary>
    public static (byte R, byte G, byte B) Accent(byte[] px)
    {
        double r = 0, g = 0, b = 0, total = 0;
        for (int i = 0; i < px.Length; i += 4)
        {
            double B = px[i], G = px[i + 1], R = px[i + 2];
            double max = Math.Max(R, Math.Max(G, B)), min = Math.Min(R, Math.Min(G, B));
            double sat = max <= 0 ? 0 : (max - min) / max;
            double weight = 0.02 + sat * sat * (max / 255.0);
            r += R * weight; g += G * weight; b += B * weight; total += weight;
        }
        r /= total; g /= total; b /= total;

        double vmax = Math.Max(r, Math.Max(g, b)), vmin = Math.Min(r, Math.Min(g, b));
        double s = vmax <= 0 ? 0 : (vmax - vmin) / vmax;
        if (s < 0.18) return (235, 235, 240);

        double hue = vmax == vmin ? 0
                   : vmax == r ? 60 * (((g - b) / (vmax - vmin)) % 6)
                   : vmax == g ? 60 * ((b - r) / (vmax - vmin) + 2)
                   : 60 * ((r - g) / (vmax - vmin) + 4);
        if (hue < 0) hue += 360;
        return FromHsv(hue, Math.Min(s * 1.15, 0.8), 1.0);
    }

    static (byte R, byte G, byte B) FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x),
            < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
