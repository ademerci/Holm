using System;
using System.Threading;
using System.Threading.Tasks;

namespace Holm.Platform;

/// <summary>
/// Dictation with Handy (github.com/cjpais/Handy): whether it's installed, starting it, and (on Windows) whether it
/// listens, from its overlay window, which is moved out of sight. Where the backend can't see that
/// (<see cref="ReportsListening"/> false), the app goes by Handy's use of the microphone.
/// </summary>
public interface IDictationPlatform
{
    /// <summary>Handy's program, or null when it isn't installed.</summary>
    string? FindHandy();

    /// <summary>Whether <see cref="ListeningChanged"/> says when Handy listens.</summary>
    bool ReportsListening { get; }

    /// <summary>Raised on the UI thread.</summary>
    event Action<bool>? ListeningChanged;

    /// <summary>Starts watching Handy, on the UI thread, whose loop the app hands in: post, invoke, "on it?", and a 1 s ticker.</summary>
    void Start(Action<Action> post, Action<Action> invoke, Func<bool> onUiThread, Func<Action, Action<bool>> ticker);
    void Stop();

    /// <summary>Handy's overlay is switched off in its settings: then nothing shows when it listens.</summary>
    bool OverlayOff();

    /// <summary>Starts Handy hidden in the tray if it isn't running.</summary>
    void Launch(string program);

    /// <summary>Fetches Handy's official installer and opens it; false where that isn't offered (the release page opens instead).</summary>
    Task<bool> InstallAsync(CancellationToken ct);
}

public sealed class NoDictationPlatform : IDictationPlatform
{
    public string? FindHandy() => null;
    public bool ReportsListening => false;
    public event Action<bool>? ListeningChanged { add { } remove { } }
    public void Start(Action<Action> post, Action<Action> invoke, Func<bool> onUiThread, Func<Action, Action<bool>> ticker) { }
    public void Stop() { }
    public bool OverlayOff() => false;
    public void Launch(string program) { }
    public Task<bool> InstallAsync(CancellationToken ct) => Task.FromResult(false);
}
