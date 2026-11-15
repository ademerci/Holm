using System;
using Avalonia.Media;

namespace Holm.App;

/// <summary>
/// The fonts the app draws with. On Windows they are the WPF app's: Segoe UI Variable for text and Segoe Fluent
/// Icons for icons. Those ship with Windows and may not be redistributed, so everywhere else text is set in Inter
/// (bundled with Avalonia) and icons come from Holm Icons (Assets/HolmIcons.ttf), which files open-licensed icons
/// under the same code points: the code that picks an icon is the same on every system. See tools/icon-font.
/// </summary>
static class Fonts
{
    static readonly FontFamily Inter = new("fonts:Inter#Inter");

    public static readonly FontFamily Text = OperatingSystem.IsWindows()
        ? new FontFamily("Segoe UI Variable Text, Segoe UI, Inter") : Inter;

    public static readonly FontFamily Display = OperatingSystem.IsWindows()
        ? new FontFamily("Segoe UI Variable Display, Segoe UI, Inter") : Inter;

    public static readonly FontFamily Icons = OperatingSystem.IsWindows()
        ? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets")
        : new FontFamily("avares://Holm.App/Assets/HolmIcons.ttf#Holm Icons");
}
