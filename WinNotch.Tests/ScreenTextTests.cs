using System.Text.Json;

namespace WinNotch.Tests;

public class ScreenTextTests
{
    [Theory]
    [InlineData(1920u, 1080u, 10000u, 1920u, 1080u)]
    [InlineData(20000u, 5000u, 10000u, 10000u, 2500u)]
    [InlineData(3000u, 12000u, 10000u, 2500u, 10000u)]
    [InlineData(100000u, 1u, 10000u, 10000u, 1u)]
    public void Fits_the_largest_side(uint width, uint height, uint max, uint fitWidth, uint fitHeight)
        => Assert.Equal((fitWidth, fitHeight), OcrText.Fit(width, height, max));

    [Fact]
    public void Joins_lines_for_pasting()
        => Assert.Equal("Meeting at 15:30\r\nRoom 2.14", OcrText.Join(new[] { "  Meeting at 15:30 ", "", "   ", "Room 2.14" }));

    [Fact]
    public void Nothing_read_is_empty() => Assert.Equal("", OcrText.Join(new string[0]));

    [Theory]
    [InlineData("", "0 lines")]
    [InlineData("one", "1 line")]
    [InlineData("one\r\ntwo\r\nthree", "3 lines")]
    public void Counts_lines(string text, string expected) => Assert.Equal(expected, OcrText.Count(text));

    [Fact]
    public void Settings_from_before_screenshot_text_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Screenshots\":false}")!;
        Assert.False(old.Screenshots);
        Assert.True(old.ScreenshotText);
    }
}
