using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// A password copied from a password manager (marked "ExcludeClipboardContentFromMonitorProcessing") is wiped from
/// the clipboard after 30 seconds, if it is still there. Holm never reads it: the format and the clipboard's
/// sequence number tell it all it needs.
/// </summary>
public partial class MainWindow
{
    const string PasswordGlyph = "";
    readonly ClipboardWipe _wipe = new();
    DispatcherTimer _wipeTimer = null!;

    void InitPasswordWipe() => _wipeTimer = MakeTimer((int)ClipboardWipe.After.TotalMilliseconds, WipePassword, oneShot: true);

    void OnClipboardForPasswords()
    {
        if (!_settings.ClearPasswords) return;
        uint seq = Native.GetClipboardSequenceNumber();
        Dispatcher.InvokeAsync(() =>
        {
            if (!_settings.ClearPasswords) return;
            if (Native.IsClipboardFormatAvailable(Native.RegisterClipboardFormat(ClipboardPrivacy.ExcludeFormat)))
            {
                _wipe.Arm(seq);
                _wipeTimer.Stop();
                _wipeTimer.Start();
            }
            else if (seq == Native.GetClipboardSequenceNumber())
            {
                _wipe.Disarm(); // something else was copied since
                _wipeTimer.Stop();
            }
        }, DispatcherPriority.Background);
    }

    void WipePassword()
    {
        if (!_settings.ClearPasswords || !_wipe.TakeDue(Native.GetClipboardSequenceNumber())) return;
        bool done = false;
        for (int attempt = 0; attempt < 3 && !done; attempt++)
        {
            try { Clipboard.Clear(); done = true; }
            catch (COMException) { Thread.Sleep(30); } // another app has it open
        }
        Diag.Log(done ? "Copied password wiped" : "Copied password: clipboard busy");
        if (done) ShowPeek(PasswordGlyph, "Password cleared from the clipboard", "", null, false, null);
    }

    void MenuClearPasswords_Click(object sender, RoutedEventArgs e)
    {
        _settings.ClearPasswords = MenuClearPasswords.IsChecked;
        _settings.Save();
        if (_settings.ClearPasswords) return;
        _wipe.Disarm();
        _wipeTimer.Stop();
    }
}
