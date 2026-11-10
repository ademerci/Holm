using WinNotch;

namespace WinNotch.Tests;

public class ChatLookTests
{
    [Theory]
    [InlineData("😂")]
    [InlineData("🙌🔥")]
    [InlineData(" ❤️ 👍 ")]
    [InlineData("👍🏽")]
    public void A_few_emoji_alone_show_big(string text) => Assert.True(ChatPage.IsBigEmoji(text));

    [Theory]
    [InlineData("")]
    [InlineData("ok")]
    [InlineData("?")]
    [InlineData("!!")]
    [InlineData("1")]
    [InlineData("haha 😂")]
    [InlineData("😂😂😂😂")]
    public void Text_or_many_emoji_stay_in_a_bubble(string text) => Assert.False(ChatPage.IsBigEmoji(text));
}
