using System;
using System.IO;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.Windows;

/// <summary>
/// The WPF app's sounds without WPF: winmm's PlaySound in place of SoundPlayer, MessageBeep in place of SystemSounds.
/// </summary>
public sealed class WindowsSoundPlatform : ISoundPlatform
{
    const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004, SND_FILENAME = 0x00020000;
    const uint MB_ICONASTERISK = 0x40, MB_ICONEXCLAMATION = 0x30;

    /// <summary>PlaySound reads a sound in memory while it plays: it must stay put until the next one.</summary>
    static GCHandle _playing;

    public void PlayAlarm()
    {
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(wav) && PlaySound(wav, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DllNotFoundException) { }
        MessageBeep(MB_ICONEXCLAMATION);
    }

    public void PlayWav(byte[] wav)
    {
        try
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0); // stop the one before, so its memory can go
            if (_playing.IsAllocated) _playing.Free();
            _playing = GCHandle.Alloc(wav, GCHandleType.Pinned);
            if (PlaySound(_playing.AddrOfPinnedObject(), IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT)) return;
        }
        catch (DllNotFoundException) { }
        MessageBeep(MB_ICONASTERISK);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")] static extern bool PlaySound(string sound, IntPtr module, uint flags);
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")] static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    [DllImport("user32.dll")] static extern bool MessageBeep(uint type);
}
