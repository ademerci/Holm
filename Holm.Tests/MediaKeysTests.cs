using Holm.Platform;

namespace WinNotch.Tests;

/// <summary>The volume step math used when Holm handles the volume keys (Settings › Pop-outs › Volume and media only in the notch).</summary>
public class CrossPlatformMediaKeysTests
{
    [Theory]
    [InlineData(0.38f, true, 0.40f)]
    [InlineData(0.38f, false, 0.36f)]
    [InlineData(0.37f, true, 0.38f)]
    [InlineData(0.37f, false, 0.36f)]
    [InlineData(0f, false, 0f)]
    [InlineData(0.01f, false, 0f)]
    [InlineData(1f, true, 1f)]
    [InlineData(0.99f, true, 1f)]
    public void A_volume_key_moves_two_percent(float level, bool up, float expected)
        => Assert.Equal(expected, MediaKeysMath.Step(level, up), 3);

    [Fact]
    public async Task Fallback_platforms_report_unsupported()
    {
        var keys = new NoMediaKeysPlatform();
        Assert.False(keys.IsSupported);
        Assert.False(keys.Start(_ => true, _ => { }));
        keys.Stop();

        var location = new NoLocationPlatform();
        Assert.False(location.IsSupported);
        Assert.Null(await location.TryGetApproximateAsync(CancellationToken.None));
    }
}
