using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Handy on Linux: found as an installed program (package or AppImage). When it listens is told by its use of the
/// microphone, which the app sees anyway; its own overlay stays, as Linux has no common way to move another app's window.
/// </summary>
public sealed class LinuxDictationPlatform : IDictationPlatform
{
    public string? FindHandy()
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin").Split(':'))
            if (folder.Length > 0 && File.Exists(Path.Combine(folder, "handy"))) return Path.Combine(folder, "handy");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (string folder in new[] { Path.Combine(home, "Applications"), Path.Combine(home, ".local", "bin") })
        {
            try
            {
                if (Directory.Exists(folder) && Directory.GetFiles(folder, "Handy*.AppImage").OrderByDescending(f => f).FirstOrDefault() is { } image) return image;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public bool ReportsListening => false;
    public event Action<bool>? ListeningChanged { add { } remove { } }
    public void Start(Action<Action> post, Action<Action> invoke, Func<bool> onUiThread, Func<Action, Action<bool>> ticker) { }
    public void Stop() { }
    public bool OverlayOff() => false;

    public void Launch(string program)
    {
        if (Process.GetProcessesByName("handy").Any()) return;
        try { Process.Start(new ProcessStartInfo(program, "--start-hidden") { UseShellExecute = false })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { WinNotch.Diag.Log("Handy didn't start: " + ex.Message); }
    }

    /// <summary>Handy comes as a package or an AppImage: its release page is where to choose.</summary>
    public Task<bool> InstallAsync(CancellationToken ct) => Task.FromResult(false);
}
