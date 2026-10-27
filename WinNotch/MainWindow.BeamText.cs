using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Beaming text: what you copied goes to a friend, someone nearby, your phone or a code, and lands on their
/// clipboard with a note. It travels as one small text file marked as text, so Holm before 1.7 simply saves it in
/// Downloads; the phone shows it on its page.
/// </summary>
public partial class MainWindow
{
    static readonly string TextInbox = Path.Combine(Path.GetTempPath(), "Holm", "Text") + Path.DirectorySeparatorChar;
    string? _pickText; // the text file the share view was opened for

    /// <summary>Beams what is on the clipboard now: the share view opens to click who gets it.</summary>
    void BeamCopiedText()
    {
        if (ClipboardVerdict() != ClipVerdict.Keep)
        {
            ShowPeek(null, "That's private (a password?) – not beamed", "", null, false, null);
            return;
        }
        string? text = ReadClipboardText();
        if (string.IsNullOrWhiteSpace(text)) { ShowPeek(null, "Copy some text first", "", null, false, null); return; }
        BeamText(text);
    }

    void BeamText(string text)
    {
        if (!_settings.Beam || _busy || _sharing || _installingUpdate) return;
        if (Encoding.UTF8.GetByteCount(text) > FileTransfer.MaxTextBytes)
        {
            ShowPeek(null, "That text is too long to beam", "Send it as a file", null, false, DimBrush);
            return;
        }
        string path;
        try
        {
            Directory.CreateDirectory(FolderPack.TempFolder);
            path = Path.Combine(FolderPack.TempFolder, "Beamed text.txt");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(FolderPack.TempFolder, $"Beamed text ({n}).txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Beam text: " + ex.GetType().Name); // never the text
            return;
        }
        ForgetPickedText();
        PickTarget(new[] { path });
        _pickText = path;
        RefreshShareTargets();
    }

    /// <summary>Sends the text file, then deletes it (for the phone, once its link closes).</summary>
    async Task BeamTextFileAsync(ShareTarget target, string path)
    {
        if (target.Place == ShareTarget.PhonePlace)
        {
            BeamToPhone(new() { path });
            _phoneZips.Add(path);
            return;
        }
        try { await SendFilesAsync(target, new() { path }, text: true); }
        finally { FolderPack.Forget(new[] { path }, FolderPack.TempFolder); }
    }

    void ForgetPickedText()
    {
        if (_pickText is not { } path) return;
        _pickText = null;
        FolderPack.Forget(new[] { path }, FolderPack.TempFolder);
    }

    /// <summary>A text arrived: onto the clipboard it goes, and the file is gone. Busy clipboard: it stays in Downloads.</summary>
    void TakeBeamedText(string path, string sender)
    {
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diag.Log("Beamed text: " + ex.GetType().Name);
            EndTask("\uE783", RedBrush, "Couldn't read the text", "From " + sender);
            return;
        }
        if (TrySetClipboard(text))
        {
            try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            EndTask("", GreenBrush, $"Text from {sender} copied", "Paste it with Ctrl+V");
            return;
        }
        // The clipboard is busy: keep it where they'd look for a file.
        string kept = Path.Combine(Native.DownloadsFolder(), Path.GetFileName(path));
        try { File.Move(path, kept, overwrite: false); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { kept = path; }
        EndTask("", GreenBrush, $"Text from {sender} saved", "The clipboard was busy · in Downloads",
                ("Show", () => { HideTask(); try { Shell.Reveal(kept); } catch (Exception ex) { Diag.Error(ex); } }));
    }

    /// <summary>Beam › Beam copied text…, and whether beamed text goes to the clipboard.</summary>
    void FillBeamTextMenu()
    {
        MenuBeamText.Items.Clear();
        MenuBeamText.IsEnabled = _settings.Beam;
        var beam = new MenuItem { Header = "Beam copied text…", IsEnabled = _settings.BeamText };
        beam.Click += (_, _) => BeamCopiedText();
        MenuBeamText.Items.Add(beam);
        var receive = new MenuItem { Header = "Beamed text goes to the clipboard", IsCheckable = true, IsChecked = _settings.BeamText };
        receive.Click += (_, _) =>
        {
            _settings.BeamText = receive.IsChecked;
            _settings.Save();
        };
        MenuBeamText.Items.Add(receive);
    }
}
