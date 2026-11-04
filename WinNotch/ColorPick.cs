using System;

namespace WinNotch;

/// <summary>The arithmetic of the color picker: hex codes, pixels, and where the magnifier goes.</summary>
internal static class ColorPick
{
    /// <summary>Pixels on each side of the one under the cursor that the magnifier shows.</summary>
    public const int Reach = 5;
    public const int Span = Reach * 2 + 1;

    /// <summary>"#0A84FF", the way design tools and CSS want it.</summary>
    public static string Hex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>The pixel at (x, y) of a 32-bit BGRA buffer, as (r, g, b).</summary>
    public static (byte R, byte G, byte B) At(byte[] bgra, int stride, int x, int y)
    {
        int i = y * stride + x * 4;
        return (bgra[i + 2], bgra[i + 1], bgra[i]);
    }

    /// <summary>How far an arrow key moves the cursor while picking: one pixel, ten with Shift. (0, 0) for other keys.</summary>
    public static (int Dx, int Dy) Step(uint virtualKey, bool shift)
    {
        int n = shift ? 10 : 1;
        return virtualKey switch
        {
            0x25 => (-n, 0), // left
            0x26 => (0, -n), // up
            0x27 => (n, 0),  // right
            0x28 => (0, n),  // down
            _ => (0, 0),
        };
    }

    /// <summary>
    /// Top-left of the magnifier: below and right of the cursor, flipped to the other side where it would leave
    /// the display. All in physical pixels.
    /// </summary>
    public static (int X, int Y) LoupeAt(int cursorX, int cursorY, int width, int height, Native.RECT display, int gap = 24)
    {
        int x = cursorX + gap, y = cursorY + gap;
        if (x + width > display.Right) x = cursorX - gap - width;
        if (y + height > display.Bottom) y = cursorY - gap - height;
        x = Math.Max(display.Left, Math.Min(x, display.Right - width));
        y = Math.Max(display.Top, Math.Min(y, display.Bottom - height));
        return (x, y);
    }
}
