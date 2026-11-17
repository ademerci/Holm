using System;
using System.Diagnostics;
using System.IO;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Sounds through the desktop's sound server: paplay (PulseAudio and PipeWire), pw-play or aplay, whichever is there.
/// The alarm is the freedesktop sound theme's, as desktops ring their own alarms.
/// </summary>
public sealed class LinuxSoundPlatform : ISoundPlatform
{
    static readonly string[] Players = { "paplay", "pw-play", "aplay" };
    static readonly string[] AlarmSounds =
    {
        "/usr/share/sounds/freedesktop/stereo/alarm-clock-elapsed.oga",
        "/usr/share/sounds/freedesktop/stereo/complete.oga",
        "/usr/share/sounds/freedesktop/stereo/bell.oga",
    };
    string? _wavFile;
    byte[]? _wavFor;

    public void PlayAlarm()
    {
        foreach (string sound in AlarmSounds)
            if (File.Exists(sound) && Play(sound, oggOnly: true)) return;
        // No sound theme installed: three short beeps, as Windows falls back to its exclamation sound.
        PlayWav(Beeps.Value);
    }

    static readonly Lazy<byte[]> Beeps = new(() => Tone(880, 0.12, 0.10, 3));

    /// <summary>A 16-bit mono WAV: <paramref name="count"/> sine beeps with short fades, gaps between them.</summary>
    internal static byte[] Tone(double hz, double beep, double gap, int count)
    {
        const int rate = 22050;
        int beepSamples = (int)(rate * beep), gapSamples = (int)(rate * gap), fade = rate / 200;
        int samples = count * (beepSamples + gapSamples);
        using var memory = new MemoryStream();
        using (var w = new BinaryWriter(memory, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(samples * 2);
            for (int b = 0; b < count; b++)
            {
                for (int i = 0; i < beepSamples; i++)
                {
                    double envelope = Math.Min(1, Math.Min(i, beepSamples - i) / (double)fade);
                    w.Write((short)(Math.Sin(2 * Math.PI * hz * i / rate) * envelope * 0.35 * short.MaxValue));
                }
                for (int i = 0; i < gapSamples; i++) w.Write((short)0);
            }
        }
        return memory.ToArray();
    }

    public void PlayWav(byte[] wav)
    {
        try
        {
            // The players want a file: the app's sound is written once to the temp folder.
            if (!ReferenceEquals(wav, _wavFor) || _wavFile == null || !File.Exists(_wavFile))
            {
                _wavFile = Path.Combine(Path.GetTempPath(), $"holm-sound-{Environment.UserName}-{wav.Length}.wav");
                File.WriteAllBytes(_wavFile, wav);
                _wavFor = wav;
            }
            Play(_wavFile, oggOnly: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The first player that is installed; aplay can't play Ogg, so it is skipped for those.</summary>
    static bool Play(string file, bool oggOnly)
    {
        foreach (string player in Players)
        {
            if (oggOnly && player == "aplay") continue;
            if (Find(player) is not { } exe) continue;
            try
            {
                var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
                if (player == "aplay") start.ArgumentList.Add("-q");
                start.ArgumentList.Add(file);
                using var process = Process.Start(start);
                if (process == null) continue;
                _ = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                return true; // it plays on by itself
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        return false;
    }

    static string? Find(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':'))
            if (folder.Length > 0 && File.Exists(Path.Combine(folder, name))) return Path.Combine(folder, name);
        return null;
    }
}
