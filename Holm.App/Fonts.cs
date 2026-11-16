using Avalonia;
using Avalonia.Media;

namespace Holm.App;

/// <summary>
/// The fonts the app draws with, the same on every system: text in Inter (bundled with Avalonia), icons from Holm
/// Icons (Assets/HolmIcons.ttf), which files open-licensed icons under the code points of Segoe Fluent Icons. Both
/// ship inside the app, so nothing depends on what a system has installed and the notch looks alike on Windows,
/// Linux. The WPF app's Segoe fonts may not be redistributed, which is why they are not used even on
/// Windows. See tools/icon-font.
/// </summary>
static class Fonts
{
    static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public static readonly FontFamily Text = Inter;
    public static readonly FontFamily Display = Inter;
    public static readonly FontFamily Icons = new("avares://Holm.App/Assets/HolmIcons.ttf#Holm Icons");

    /// <summary>
    /// Text is drawn the same way everywhere: greyscale antialiasing (subpixel colour fringes look wrong on a
    /// see-through window and differ per screen), no hinting (which each system's rasteriser does its own way),
    /// glyphs placed where the layout puts them. The window and everything in it inherit this.
    /// </summary>
    public static void ApplyRendering(Visual root)
    {
        TextOptions.SetTextRenderingMode(root, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(root, TextHintingMode.None);
        TextOptions.SetBaselinePixelAlignment(root, BaselinePixelAlignment.Unaligned);
        RenderOptions.SetEdgeMode(root, EdgeMode.Antialias);
    }
}
