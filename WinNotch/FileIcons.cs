using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch;

/// <summary>The icons Explorer shows for files and folders. Call on the UI thread.</summary>
internal static class FileIcons
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr icon);

    const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    /// <summary>Types whose files each have their own icon; every other type shares one.</summary>
    static readonly HashSet<string> OwnIcons = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".ico", ".url", ".appref-ms" };
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The 32-pixel icon, or null if Windows has none.</summary>
    public static ImageSource? For(string path)
    {
        bool folder = Directory.Exists(path);
        bool exists = folder || File.Exists(path);
        string extension = Path.GetExtension(path);
        string key = folder ? "\\folder" : exists && OwnIcons.Contains(extension) ? path : extension.ToLowerInvariant();
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var info = new SHFILEINFO();
        // A file that's gone still gets its type's icon, by its name alone.
        uint flags = SHGFI_ICON | SHGFI_LARGEICON | (exists ? 0 : SHGFI_USEFILEATTRIBUTES);
        ImageSource? icon = null;
        if (SHGetFileInfo(path, folder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) != IntPtr.Zero
            && info.hIcon != IntPtr.Zero)
        {
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                icon = source;
            }
            catch (Exception ex) { Diag.Log("File icon: " + ex.Message); }
            finally { DestroyIcon(info.hIcon); }
        }
        if (Cache.Count > 200) Cache.Clear();
        Cache[key] = icon;
        return icon;
    }
}
