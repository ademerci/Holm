using Holm.Platform;

namespace Holm.Tests;

/// <summary>The privacy markers password managers put on the clipboard, on each system.</summary>
public class ClipboardFormatsTests
{
    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing")] // Windows: KeePass, 1Password, Bitwarden
    [InlineData("x-kde-passwordManagerHint")]                      // Linux: KeePassXC, KDE
    public void Password_managers_mark_content_private(string marker)
        => Assert.Equal(ClipPrivacy.Private, ClipboardFormats.Judge(new[] { "text/plain", marker }));

    [Theory]
    [InlineData("Clipboard Viewer Ignore")]
    public void Content_apps_ask_to_ignore_is_skipped(string marker)
        => Assert.Equal(ClipPrivacy.Skip, ClipboardFormats.Judge(new[] { marker, "UTF8_STRING" }));

    [Fact]
    public void Windows_history_switch_decides_when_set()
    {
        Assert.Equal(ClipPrivacy.Skip, ClipboardFormats.Judge(new[] { "Text" }, historyAllowed: false));
        Assert.Equal(ClipPrivacy.Keep, ClipboardFormats.Judge(new[] { "Text" }, historyAllowed: true));
        Assert.Equal(ClipPrivacy.Keep, ClipboardFormats.Judge(new[] { "Text", "image/png" }));
    }
}
