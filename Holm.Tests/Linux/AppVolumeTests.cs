using Holm.Platform.Linux;
using WinNotch;

namespace Holm.Tests.Linux;

/// <summary>PulseAudio's sound streams as the mixer's apps, and where their icons are found.</summary>
public class LinuxAppVolumeTests : IDisposable
{
    readonly string _folder = Path.Combine(Path.GetTempPath(), "holm-icons-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch { }
    }

    [Fact]
    public void Streams_of_one_program_are_one_app_and_event_sounds_are_left_out()
    {
        var sessions = new List<AudioSessionInfo>
        {
            LinuxAppVolumePlatform.Session("Firefox", "firefox", "100", "", 0.5f, false, corked: false),
            LinuxAppVolumePlatform.Session("Firefox", "firefox", "101", "", 0.8f, false, corked: true),
            LinuxAppVolumePlatform.Session("Spotify", "spotify", "200", "music", 1f, true, corked: true),
            LinuxAppVolumePlatform.Session("GNOME Shell", "gnome-shell", "300", "event", 1f, false, corked: false),
        };

        var apps = AppVolumeList.Build(sessions);

        Assert.Equal(new[] { "Firefox", "Spotify" }, apps.Select(a => a.Name));
        Assert.True(apps[0].Playing);
        Assert.Equal(0.8f, apps[0].Volume);
        Assert.Equal(2, apps[0].Sessions.Count);
        Assert.True(apps[1].Muted);
        Assert.False(apps[1].Playing);
    }

    [Fact]
    public void Icons_come_from_the_hicolor_theme_at_a_fitting_size()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "256x256", "apps"));
        Directory.CreateDirectory(Path.Combine(_folder, "48x48", "apps"));
        File.WriteAllBytes(Path.Combine(_folder, "256x256", "apps", "firefox.png"), new byte[1]);
        File.WriteAllBytes(Path.Combine(_folder, "48x48", "apps", "firefox.png"), new byte[1]);

        Assert.Equal(Path.Combine(_folder, "48x48", "apps", "firefox.png"), LinuxAppVolumePlatform.FindIcon("firefox", new[] { _folder }));
        Assert.Null(LinuxAppVolumePlatform.FindIcon("nothing", new[] { _folder }));
        Assert.Null(LinuxAppVolumePlatform.FindIcon("../48x48/apps/firefox", new[] { _folder }));
        Assert.Null(LinuxAppVolumePlatform.FindIcon("", new[] { _folder }));
    }
}
