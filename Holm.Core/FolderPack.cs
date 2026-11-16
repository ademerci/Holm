using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace WinNotch;

/// <summary>
/// Folders dropped on a person go as a ZIP: packed into Holm's temp folder first, deleted once sent (or not).
/// Links inside the folder aren't followed, so a folder can't pull in half the disk or loop.
/// </summary>
internal static class FolderPack
{
    public static string TempFolder => Path.Combine(Path.GetTempPath(), "Holm", "Beam");

    /// <summary>"Holiday photos.zip" for C:\…\Holiday photos, "E.zip" for a drive; "(2)" and on if taken.</summary>
    public static string ZipPathFor(string folder, string tempFolder)
    {
        string trimmed = folder.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        if (name.Length == 0) name = trimmed.TrimEnd(':');
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        if (name.Length == 0) name = "Folder";
        string path = Path.Combine(tempFolder, name + ".zip");
        for (int n = 2; File.Exists(path); n++) path = Path.Combine(tempFolder, $"{name} ({n}).zip");
        return path;
    }

    /// <summary>The files a folder holds, without following links, with the folder's own name in front.</summary>
    public static List<(string Path, string Entry)> FilesOf(string folder)
    {
        var root = new DirectoryInfo(folder);
        string top = root.Name.TrimEnd(':', '\\', '/');
        var files = new List<(string, string)>();
        var pending = new Stack<(DirectoryInfo Dir, string Prefix)>();
        pending.Push((root, top));
        while (pending.Count > 0)
        {
            var (dir, prefix) = pending.Pop();
            IEnumerable<FileSystemInfo> children;
            try { children = dir.EnumerateFileSystemInfos().ToList(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { continue; }
            foreach (var child in children.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0) continue; // a link or junction
                string entry = prefix + "/" + child.Name;
                if (child is DirectoryInfo sub) pending.Push((sub, entry));
                else files.Add((child.FullName, entry));
            }
        }
        return files;
    }

    /// <summary>
    /// Packs a folder; returns the ZIP's path and how many files couldn't be read (they're left out). Cancelling
    /// removes the half-written ZIP.
    /// </summary>
    public static (string Zip, int Skipped) Pack(string folder, string tempFolder, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(tempFolder);
        string zipPath = ZipPathFor(folder, tempFolder);
        var files = FilesOf(folder);
        long total = Math.Max(1, files.Sum(f => SafeLength(f.Path))), done = 0;
        int skipped = 0;
        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            if (files.Count == 0) zip.CreateEntry(new DirectoryInfo(folder).Name + "/"); // an empty folder stays a folder
            foreach (var (path, entry) in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var item = zip.CreateEntry(entry, CompressionLevel.Fastest); // photos and videos are packed already
                    item.LastWriteTime = File.GetLastWriteTime(path);
                    using var output = item.Open();
                    input.CopyTo(output);
                    done += input.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { skipped++; }
                progress?.Report((double)done / total);
            }
        }
        catch
        {
            try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return (zipPath, skipped);
    }

    static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    /// <summary>Deletes packed ZIPs; only ever files inside <paramref name="tempFolder"/>.</summary>
    public static void Forget(IEnumerable<string> zips, string tempFolder)
    {
        string root = Path.GetFullPath(tempFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var zip in zips)
        {
            if (!Path.GetFullPath(zip).StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(zip); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
