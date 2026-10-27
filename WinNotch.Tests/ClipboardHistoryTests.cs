using System;
using System.Linq;
using System.Text.Json;

namespace WinNotch.Tests;

public class ClipboardHistoryTests
{
    static ClipEntry Text(string text) => new() { Key = ClipboardHistory.TextKey(text), Text = text, CopiedAt = DateTime.UtcNow };

    [Fact]
    public void Newest_comes_first()
    {
        var history = new ClipboardHistory();
        history.Add(Text("one"));
        history.Add(Text("two"));
        Assert.Equal(new[] { "two", "one" }, history.Items.Select(e => e.Text));
    }

    [Fact]
    public void Keeps_the_last_twenty_and_hands_back_the_rest()
    {
        var history = new ClipboardHistory();
        for (int i = 0; i < 20; i++) Assert.Empty(history.Add(Text("item " + i)));
        var gone = history.Add(Text("item 20"));
        Assert.Equal(20, history.Items.Count);
        Assert.Equal("item 0", Assert.Single(gone).Text); // its picture file can be deleted
        Assert.Equal("item 20", history.Items[0].Text);
    }

    [Fact]
    public void Copying_the_same_again_moves_it_up_instead_of_adding_it_twice()
    {
        var history = new ClipboardHistory();
        history.Add(Text("one"));
        history.Add(Text("two"));
        Assert.True(history.Bump(ClipboardHistory.TextKey("one"), DateTime.UtcNow));
        Assert.Equal(new[] { "one", "two" }, history.Items.Select(e => e.Text));
        Assert.False(history.Bump(ClipboardHistory.TextKey("three"), DateTime.UtcNow));

        var gone = history.Add(Text("two"));
        Assert.Single(gone);
        Assert.Equal(new[] { "two", "one" }, history.Items.Select(e => e.Text));
    }

    [Fact]
    public void Remove_and_clear()
    {
        var history = new ClipboardHistory();
        history.Add(Text("one"));
        history.Add(Text("two"));
        Assert.Equal("one", history.Remove(ClipboardHistory.TextKey("one"))!.Text);
        Assert.Null(history.Remove(ClipboardHistory.TextKey("one")));
        Assert.Single(history.Clear());
        Assert.Empty(history.Items);
    }

    [Fact]
    public void Keys_tell_texts_and_pictures_apart()
    {
        Assert.Equal(ClipboardHistory.TextKey("a"), ClipboardHistory.TextKey("a"));
        Assert.NotEqual(ClipboardHistory.TextKey("a"), ClipboardHistory.TextKey("A"));
        var pixels = new byte[] { 1, 2, 3, 4 };
        Assert.Equal(ClipboardHistory.ImageKey(pixels, 1, 1), ClipboardHistory.ImageKey(new byte[] { 1, 2, 3, 4 }, 1, 1));
        Assert.NotEqual(ClipboardHistory.ImageKey(pixels, 1, 1), ClipboardHistory.ImageKey(new byte[] { 1, 2, 3, 5 }, 1, 1));
        Assert.NotEqual(ClipboardHistory.ImageKey(pixels, 1, 1), ClipboardHistory.ImageKey(pixels, 2, 1));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  \r\n ", false)]
    [InlineData("x", true)]
    public void Only_texts_worth_keeping(string? text, bool worth) => Assert.Equal(worth, ClipboardHistory.Worth(text));

    [Fact]
    public void Huge_texts_are_left_out() => Assert.False(ClipboardHistory.Worth(new string('x', ClipboardHistory.MaxTextLength + 1)));

    [Theory]
    [InlineData("hello", 90, "hello")]
    [InlineData("  line one\r\n\r\n  line two\t\tend  ", 90, "line one line two end")]
    [InlineData("abcdefghij", 5, "abcde…")]
    [InlineData("abc  defghij", 5, "abc d…")]
    [InlineData("abcd efghij", 5, "abcd…")]
    public void Previews_fit_one_line(string text, int max, string expected) => Assert.Equal(expected, ClipboardHistory.Preview(text, max));

    [Theory]
    [InlineData(0, "now")]
    [InlineData(59, "now")]
    [InlineData(60, "1 min")]
    [InlineData(59 * 60 + 59, "59 min")]
    [InlineData(3600, "1 h")]
    [InlineData(25 * 3600, "1 d")]
    public void Says_how_long_ago(int seconds, string expected)
    {
        var now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, ClipboardHistory.Ago(now.AddSeconds(-seconds), now));
    }

    [Theory]
    [InlineData(true, false, null, "Private")]
    [InlineData(true, true, true, "Private")]
    [InlineData(false, true, null, "Skip")]
    [InlineData(false, false, false, "Skip")]
    [InlineData(false, false, true, "Keep")]
    [InlineData(false, false, null, "Keep")]
    public void Private_content_is_never_kept(bool exclude, bool viewerIgnore, bool? history, string expected)
        => Assert.Equal(expected, ClipboardPrivacy.Judge(exclude, viewerIgnore, history).ToString());

    [Fact]
    public void Settings_from_before_the_clipboard_history_still_load()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Animations\":false,\"Calculator\":false}")!;
        Assert.False(old.Calculator);
        Assert.True(old.ClipboardHistory);
    }

    [Fact]
    public void The_history_is_never_saved_with_the_settings()
        => Assert.DoesNotContain("Clip", JsonSerializer.Serialize(new AppSettings()).Replace("\"ClipboardHistory\":true", ""));
}
