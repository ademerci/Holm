using WinNotch;

namespace WinNotch.Tests;

/// <summary>The volume keys Holm takes over (Settings › Pop-outs › Volume and media only in the notch) step like Windows.</summary>
public class MediaKeysTests
{
    [Theory]
    [InlineData(0.38f, true, 0.40f)]
    [InlineData(0.38f, false, 0.36f)]
    [InlineData(0.37f, true, 0.38f)] // snapped to an even step, like Windows
    [InlineData(0.37f, false, 0.36f)]
    [InlineData(0f, false, 0f)]
    [InlineData(0.01f, false, 0f)]
    [InlineData(1f, true, 1f)]
    [InlineData(0.99f, true, 1f)]
    public void A_volume_key_moves_two_percent(float level, bool up, float expected)
        => Assert.Equal(expected, MediaKeys.Step(level, up), 3);

    [Theory]
    [InlineData(0xAD, true)] [InlineData(0xAE, true)] [InlineData(0xAF, true)] [InlineData(0xB3, true)]
    [InlineData(0x14, false)] [InlineData(0x41, false)] [InlineData(0xB4, false)] [InlineData(0xAC, false)]
    public void Only_volume_and_media_keys_are_looked_at(int vk, bool ours) => Assert.Equal(ours, MediaKeys.IsOurs(vk));
}
