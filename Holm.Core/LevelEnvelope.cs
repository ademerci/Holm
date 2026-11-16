using System;

namespace WinNotch;

/// <summary>
/// How loud the sound is, as the equalizer shows it. The same logic as the Dynamic Island Windhawk mod: the level of
/// each short chunk (its RMS), a quick attack and a slow release, and the bars read the history a few chunks apart so
/// they move one after another. On top, a slow automatic gain so quiet music moves the bars as much as loud music.
/// Pure arithmetic, no audio API: the tests drive it with made-up levels.
/// </summary>
internal sealed class LevelEnvelope
{
    /// <summary>How many chunk levels are kept.</summary>
    public const int History = 64;
    /// <summary>Chunks between one bar and the next (each chunk is about 10 ms).</summary>
    public const int Step = 4;
    /// <summary>The mod's envelope: quick to rise for beats, slow to fall.</summary>
    public const float Attack = 0.7f, Release = 0.15f;

    readonly float[] _levels = new float[History];
    int _write;
    float _peak = MinPeak;
    const float MinPeak = 0.02f, PeakFall = 0.995f;

    /// <summary>Takes the level of one chunk (RMS of its samples, 0 to 1).</summary>
    public void Push(float rms)
    {
        rms = float.IsFinite(rms) ? Math.Clamp(rms, 0, 1) : 0;
        // The automatic gain: the loudest recent chunk counts as full height, and that falls back slowly.
        _peak = Math.Max(Math.Max(rms, _peak * PeakFall), MinPeak);
        float level = rms < 0.0005f ? 0 : MathF.Pow(Math.Clamp(rms / _peak, 0, 1), 0.8f);
        float last = _levels[(_write + History - 1) % History];
        float smoothed = level > last ? last + (level - last) * Attack : last + (level - last) * Release;
        lock (_levels)
        {
            _levels[_write % History] = smoothed;
            _write = (_write + 1) % History;
        }
    }

    /// <summary>The level bar <paramref name="bar"/> of <paramref name="bars"/> shows: the last bar the newest chunk.</summary>
    public float Bar(int bar, int bars)
    {
        int back = (bars - 1 - Math.Clamp(bar, 0, bars - 1)) * Step + 1;
        lock (_levels) return _levels[(_write + History * 2 - back) % History];
    }

    public void Clear()
    {
        lock (_levels) Array.Clear(_levels);
        _peak = MinPeak;
    }
}
