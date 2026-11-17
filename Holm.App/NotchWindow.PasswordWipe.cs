using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// A password copied from a password manager (marked private on the clipboard: Windows' monitor-exclusion format,
/// KDE's password hint) is wiped after 30 seconds if it is still there. Holm never reads it:
/// the formats and the count of clipboard changes tell it all it needs (the WPF app's MainWindow.PasswordWipe.cs).
/// </summary>
public sealed partial class NotchWindow
{
    const string PasswordGlyph = "";
    readonly ClipboardWipe _wipe = new();
    readonly DispatcherTimer _wipeTimer = new() { Interval = ClipboardWipe.After };
    uint _clipSequence;

    /// <summary>The clipboard is watched while the history, the password wipe, Beam (copied codes) or screenshots need it.</summary>
    void FollowClipboard()
    {
        if (_settings.ClipboardHistory || _settings.ClearPasswords || _settings.Beam || _settings.Screenshots) _clipboardPlatform.Start();
        else _clipboardPlatform.Stop();
    }

    async Task CheckPasswordAsync(uint sequence)
    {
        if (!_settings.ClearPasswords || Clipboard is not { } clipboard) return;
        try
        {
            if (await PrivacyOfClipboardAsync(clipboard) == ClipPrivacy.Private)
            {
                _wipe.Arm(sequence);
                _wipeTimer.Stop();
                _wipeTimer.Start();
            }
            else if (sequence == _clipSequence)
            {
                _wipe.Disarm(); // something else was copied since
                _wipeTimer.Stop();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { Diag.Log("Copied password: " + ex.GetType().Name); }
    }

    async void WipePassword()
    {
        _wipeTimer.Stop();
        if (!_settings.ClearPasswords || !_wipe.TakeDue(_clipSequence) || Clipboard is not { } clipboard) return;
        bool done = false;
        for (int attempt = 0; attempt < 3 && !done; attempt++)
        {
            try { await clipboard.ClearAsync(); done = true; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { await Task.Delay(30); } // another app has it open
        }
        Diag.Log(done ? "Copied password wiped" : "Copied password: clipboard busy");
        if (done) ShowPeek(PasswordGlyph, "Password cleared from the clipboard", "", null);
    }

    void SetClearPasswords(bool on)
    {
        _settings.ClearPasswords = on;
        _settings.Save();
        FollowClipboard();
        if (on) return;
        _wipe.Disarm();
        _wipeTimer.Stop();
    }
}
