using System;

namespace WinNotch;

/// <summary>
/// Pauses the music when a call starts and plays it again when the call ends, but only music Holm paused itself:
/// if you resume it during the call, or switch to another player, Holm leaves it alone.
/// </summary>
internal sealed class CallPause
{
    string? _call;      // the call going on (its app), if any
    string? _paused;    // the player Holm paused, while it holds it
    bool _sawPaused;    // the player has reported "paused" since: a "playing" after that is you, not lag

    /// <summary>Holm paused the music for the call going on.</summary>
    public bool Holding => _paused != null;

    /// <summary>
    /// The call going on (null for none) and what the player is doing. True when the music should be paused
    /// now (<paramref name="pause"/>) or played again (<paramref name="resume"/>).
    /// </summary>
    public void Update(string? call, bool playing, string player, out bool pause, out bool resume)
    {
        pause = resume = false;
        if (call != null && _call == null)
        {
            _call = call;
            if (playing && player.Length > 0)
            {
                _paused = player;
                _sawPaused = false;
                pause = true;
            }
            return;
        }
        if (call == null && _call != null)
        {
            _call = null;
            // Only the same player, and only if it is still paused: you may have stopped it yourself meanwhile.
            resume = _paused != null && !playing && string.Equals(_paused, player, StringComparison.OrdinalIgnoreCase);
            _paused = null;
            return;
        }
        if (call != null) _call = call; // another call app took over: still one call
        Follow(playing, player);
    }

    /// <summary>What the player does during the call: once it stopped, playing again (or another player) is you.</summary>
    void Follow(bool playing, string player)
    {
        if (_paused == null) return;
        if (!string.Equals(_paused, player, StringComparison.OrdinalIgnoreCase) && player.Length > 0)
        {
            _paused = null;
            return;
        }
        if (!playing) _sawPaused = true;
        else if (_sawPaused) _paused = null;
    }

    /// <summary>Switched off: forget the call and the music.</summary>
    public void Reset()
    {
        _call = _paused = null;
        _sawPaused = false;
    }
}
