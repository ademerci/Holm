using System.Xml.Linq;
using Holm.Platform.Linux;

namespace Holm.Tests.Linux;

/// <summary>The sign-in entry of Linux, written into a temporary folder on every system.</summary>
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
}
