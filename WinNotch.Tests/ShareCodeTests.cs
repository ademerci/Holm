using WinNotch.Sharing;

namespace WinNotch.Tests;

public class ShareCodeTests
{
    [Fact]
    public void Format_and_TryFind_round_trip()
    {
        for (int i = 0; i < 500; i++)
        {
            var secret = ShareCode.NewSecret();
            string code = ShareCode.Format(secret);

            Assert.Matches("^WN-[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{5}-[A-Z2-7]{6}$", code);
            Assert.True(ShareCode.TryFind(code, out var parsed, out var normalized));
            Assert.Equal(secret, parsed);
            Assert.Equal(code, normalized);
        }
    }

    [Fact]
    public void TryFind_finds_a_code_inside_a_chat_message_in_any_case()
    {
        var secret = ShareCode.NewSecret();
        string code = ShareCode.Format(secret);
        string message = $"hey, here's the file 👉 WinNotch AirDrop code: {code.ToLowerInvariant()} (expires soon)";

        Assert.True(ShareCode.TryFind(message, out var parsed, out var normalized));
        Assert.Equal(secret, parsed);
        Assert.Equal(code, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello world")]
    [InlineData("WN-ABCDE-FGHIJ")]
    [InlineData("WN-ABCDE-FGHIJ-KLMNO-PQRST-UVWXY")]          // last group too short
    [InlineData("WN-ABCDE-FGHIJ-KLMNO-PQRST-UVWXYZ1")]        // glued to more characters
    [InlineData("WN-ABCDE-FGHIJ-KLMNO-PQRST-UVWX18")]         // 1 and 8 aren't base32
    [InlineData("XWN-ABCDE-FGHIJ-KLMNO-PQRST-UVWXYZ")]        // glued to a letter in front
    public void TryFind_rejects_text_without_a_code(string? text)
    {
        Assert.False(ShareCode.TryFind(text, out _, out _));
    }

    [Fact]
    public void Key_and_relay_token_differ_and_are_stable()
    {
        var secret = ShareCode.NewSecret();
        Assert.Equal(32, ShareCode.Key(secret).Length);
        Assert.Equal(32, ShareCode.RelayToken(secret).Length);
        Assert.NotEqual(ShareCode.Key(secret), ShareCode.RelayToken(secret));
        Assert.Equal(ShareCode.Key(secret), ShareCode.Key((byte[])secret.Clone()));
        Assert.NotEqual(ShareCode.Key(secret), ShareCode.Key(ShareCode.NewSecret()));
    }
}
