using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WinNotch;

/// <summary>
/// The shelf: files and folders parked in the notch to drag out again later. Only their paths are kept (in the
/// settings); nothing is copied or moved until you drag them somewhere.
/// </summary>
internal static class Shelf
{
    public const int Max = 30;

    /// <summary>Adds the paths that exist and aren't on it yet; the oldest make room past <see cref="Max"/>. Returns how many were added.</summary>
    public static int Add(List<string> shelf, IEnumerable<string> paths, Func<string, bool>? exists = null)
    {
        exists ??= Exists;
        int added = 0;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !exists(path)) continue;
            if (shelf.Contains(path, StringComparer.OrdinalIgnoreCase)) continue;
            shelf.Add(path);
            added++;
        }
        if (shelf.Count > Max) shelf.RemoveRange(0, shelf.Count - Max);
        return added;
    }

    /// <summary>Takes the paths off the shelf. Returns how many were on it.</summary>
    public static int Remove(List<string> shelf, IEnumerable<string> paths)
    {
        var gone = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return shelf.RemoveAll(gone.Contains);
    }

    /// <summary>Forgets files that were moved or deleted meanwhile. Returns how many.</summary>
    public static int Prune(List<string> shelf, Func<string, bool>? exists = null)
    {
        exists ??= Exists;
        return shelf.RemoveAll(p => !exists(p));
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>"Report.pdf", or "D:\" for a drive.</summary>
    public static string DisplayName(string path)
    {
        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : path;
    }
}
