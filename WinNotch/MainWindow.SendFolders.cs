using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace WinNotch;

/// <summary>
/// A folder dropped on a person goes as a ZIP: packed into Holm's temp folder, sent like any file, and deleted
/// afterwards (for the phone, once its link closes).
/// </summary>
public partial class MainWindow
{
    const string FolderGlyph = "";
    readonly List<string> _phoneZips = new();

    async Task PackAndSendAsync(ShareTarget target, List<string> files, List<string> folders)
    {
        var cts = BeginTask();
        string name = folders.Count == 1 ? Path.GetFileName(folders[0].TrimEnd('\\', '/')) is { Length: > 0 } one ? one : folders[0] : $"{folders.Count} folders";
        ShowTask(FolderGlyph, ShareBlue, $"Packing {name}…", "As a ZIP, to send", 0, null, ("Cancel", CancelTask));
        var zips = new List<string>();
        int skipped = 0;
        try
        {
            for (int i = 0; i < folders.Count; i++)
            {
                int index = i;
                var progress = new Progress<double>(p => ShowProgress($"Packing {name}", "As a ZIP, to send", (index + p) / folders.Count, sending: true));
                var (zip, missed) = await Task.Run(() => FolderPack.Pack(folders[index], FolderPack.TempFolder, progress, cts.Token));
                zips.Add(zip);
                skipped += missed;
            }
        }
        catch (Exception ex)
        {
            FolderPack.Forget(zips, FolderPack.TempFolder);
            Fail(ex, cts, "Couldn't pack the folder");
            return;
        }
        if (skipped > 0) Diag.Log($"Packing: {skipped} file(s) couldn't be read and were left out");

        var all = files.Concat(zips).ToList();
        if (target.Place == ShareTarget.PhonePlace)
        {
            _busy = false;
            BeamToPhone(all);
            _phoneZips.AddRange(zips); // until the link closes
            return;
        }
        try { await SendFilesAsync(target, all); }
        finally { FolderPack.Forget(zips, FolderPack.TempFolder); }
    }

    /// <summary>The phone's link closed: the ZIPs made for it can go.</summary>
    void ForgetPhoneZips()
    {
        FolderPack.Forget(_phoneZips, FolderPack.TempFolder);
        _phoneZips.Clear();
    }

    /// <summary>ZIPs left over from a Holm that was closed mid-send.</summary>
    static void ForgetLeftoverZips()
    {
        try
        {
            if (Directory.Exists(FolderPack.TempFolder))
                FolderPack.Forget(Directory.GetFiles(FolderPack.TempFolder, "*.zip"), FolderPack.TempFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    void MenuSendFolders_Click(object sender, RoutedEventArgs e)
    {
        _settings.SendFolders = MenuSendFolders.IsChecked;
        _settings.Save();
    }
}
