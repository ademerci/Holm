using System.Xml.Linq;
using Holm.Platform.Linux;
using Holm.Platform.MacOS;

namespace Holm.Tests.Linux;

/// <summary>The sign-in entries of Linux and macOS, written into a temporary folder on every system.</summary>
public class AutostartTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "holm-autostart-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    [Fact]
    public void Linux_entry_is_written_recognised_and_removed()
    {
        var autostart = new LinuxAutostartPlatform(Path.Combine(_folder, "autostart", "holm.desktop"));
        Assert.False(autostart.IsEnabled);
        autostart.Set(true);
        Assert.True(autostart.IsEnabled);
        autostart.Set(false);
        Assert.False(autostart.IsEnabled);
        autostart.Set(false); // already off: nothing to do, nothing thrown
    }

    [Fact]
    public void Linux_exec_line_quotes_and_escapes_each_argument()
        => Assert.Equal("\"/opt/my apps/Holm\" \"a\\\"b\\$c\"", LinuxAutostartPlatform.Exec(new[] { "/opt/my apps/Holm", "a\"b$c" }));

    [Fact]
    public void Mac_launch_agent_is_valid_xml_and_recognised()
    {
        string file = Path.Combine(_folder, "LaunchAgents", "app.holm.notch.plist");
        var autostart = new MacAutostartPlatform(file);
        Assert.False(autostart.IsEnabled);
        autostart.Set(true);
        Assert.True(autostart.IsEnabled);

        var plist = XDocument.Parse(MacAutostartPlatform.Plist(new[] { "/Applications/Holm & Co/Holm" }), LoadOptions.None);
        Assert.Contains(plist.Descendants("string"), e => e.Value == "/Applications/Holm & Co/Holm");

        autostart.Set(false);
        Assert.False(File.Exists(file));
    }
}
