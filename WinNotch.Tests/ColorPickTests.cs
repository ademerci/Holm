using System.Text.Json;

namespace WinNotch.Tests;

public class ColorPickTests
{
    [Theory]
    [InlineData(0, 0, 0, "#000000")]
    [InlineData(255, 255, 255, "#FFFFFF")]
    [InlineData(10, 132, 255, "#0A84FF")]
    public void Hex_codes(byte r, byte g, byte b, string expected) => Assert.Equal(expected, ColorPick.Hex(r, g, b));

    [Fact]
    public void Reads_a_pixel_from_a_bgra_buffer()
    {
        // 2 × 2 pixels, a stride with padding
        int stride = 12;
        var bgra = new byte[stride * 2];
        bgra[1 * stride + 1 * 4 + 0] = 0xFF; // blue
        bgra[1 * stride + 1 * 4 + 1] = 0x84; // green
        bgra[1 * stride + 1 * 4 + 2] = 0x0A; // red
        Assert.Equal(((byte)0x0A, (byte)0x84, (byte)0xFF), ColorPick.At(bgra, stride, 1, 1));
        Assert.Equal(((byte)0, (byte)0, (byte)0), ColorPick.At(bgra, stride, 0, 1));
    }

    static readonly Native.RECT Display = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

    [Fact]
    public void Magnifier_sits_below_right_of_the_cursor() => Assert.Equal((524, 324), ColorPick.LoupeAt(500, 300, 140, 160, Display));

    [Fact]
    public void Magnifier_flips_at_the_right_and_bottom_edges()
    {
        Assert.Equal((1900 - 24 - 140, 324), ColorPick.LoupeAt(1900, 300, 140, 160, Display));
        Assert.Equal((524, 1000 - 24 - 160), ColorPick.LoupeAt(500, 1000, 140, 160, Display));
    }

    [Fact]
    public void Magnifier_stays_on_a_display_left_of_the_main_one()
    {
        var left = new Native.RECT { Left = -2560, Top = -200, Right = 0, Bottom = 1240 };
        Assert.Equal((-2560 + 10 + 24, -200 + 24), ColorPick.LoupeAt(-2560 + 10, -200, 140, 160, left));
        Assert.Equal((-10 - 24 - 140, 1240 - 10 - 24 - 160), ColorPick.LoupeAt(-10, 1240 - 10, 140, 160, left));
    }

    [Fact]
    public void Settings_from_before_the_color_picker_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"ClearPasswords\":false}")!;
        Assert.False(old.ClearPasswords);
        Assert.True(old.ColorPicker);
    }
}
