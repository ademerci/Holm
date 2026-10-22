using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinNotch;

/// <summary>An app from the Start menu's app list. <see cref="Id"/> is its AppUserModelID or a path.</summary>
internal sealed record ShellApp(string Name, string Id);

/// <summary>Start menu apps, their icons, and opening things the way Explorer does.</summary>
internal static class Shell
{
    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bc, ref Guid bhid, ref Guid riid, out IntPtr handler);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("70629033-e363-4a28-a567-0db78006e6d7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumShellItems
    {
        [PreserveSig] int Next(uint count, out IShellItem item, out uint fetched);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    struct DIBSECTION
    {
        public BITMAP dsBm;
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        public uint dsBitfield0, dsBitfield1, dsBitfield2; public IntPtr dshSection; public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHCreateItemFromParsingName(string path, IntPtr bc, ref Guid riid, out IShellItem item);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(IntPtr handle, int size, out DIBSECTION section);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

    const uint SIGDN_NORMALDISPLAY = 0, SIGDN_PARENTRELATIVEPARSING = 0x80018001;
    const int SIIGBF_ICONONLY = 0x4;
    static Guid IID_IShellItem = typeof(IShellItem).GUID;
    static Guid IID_IEnumShellItems = typeof(IEnumShellItems).GUID;
    static Guid BHID_EnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    /// <summary>
    /// The first Start menu app whose name is one of <paramref name="names"/> (desktop apps, Store apps and
    /// web apps installed from Edge or Chrome alike). Walks a few hundred entries, so call it off the UI thread.
    /// </summary>
    public static ShellApp? FindApp(IReadOnlyList<string> names)
    {
        if (SHCreateItemFromParsingName("shell:AppsFolder", IntPtr.Zero, ref IID_IShellItem, out var folder) != 0) return null;
        IntPtr handler = IntPtr.Zero;
        try
        {
            if (folder.BindToHandler(IntPtr.Zero, ref BHID_EnumItems, ref IID_IEnumShellItems, out handler) != 0) return null;
            var items = (IEnumShellItems)Marshal.GetObjectForIUnknown(handler);
            try
            {
                while (items.Next(1, out var item, out uint fetched) == 0 && fetched == 1)
                {
                    try
                    {
                        string? name = DisplayName(item, SIGDN_NORMALDISPLAY);
                        if (name == null) continue;
                        foreach (var wanted in names)
                            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)
                                && DisplayName(item, SIGDN_PARENTRELATIVEPARSING) is { Length: > 0 } id)
                                return new ShellApp(name, id);
                    }
                    finally { Marshal.ReleaseComObject(item); }
                }
            }
            finally { Marshal.ReleaseComObject(items); }
        }
        finally
        {
            if (handler != IntPtr.Zero) Marshal.Release(handler);
            Marshal.ReleaseComObject(folder);
        }
        return null;
    }

    static string? DisplayName(IShellItem item, uint sigdn)
    {
        if (item.GetDisplayName(sigdn, out IntPtr text) != 0 || text == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    /// <summary>Shell parsing name of a Start menu app, for <see cref="IconOf"/> and <see cref="Open"/>.</summary>
    public static string AppTarget(string id) => @"shell:AppsFolder\" + id;

    /// <summary>
    /// The icon Explorer shows for a file, a program or a Start menu app, with its transparency.
    /// Frozen, so it can be made on a worker thread.
    /// </summary>
    public static BitmapSource? IconOf(string target, int size)
    {
        var iid = IID_IShellItem;
        if (SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out var item) != 0) return null;
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            if (((IShellItemImageFactory)item).GetImage(new SIZE { cx = size, cy = size }, SIIGBF_ICONONLY, out hbitmap) != 0) return null;
            return ToBitmapSource(hbitmap);
        }
        catch { return null; }
        finally
        {
            if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
            Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>
    /// Imaging.CreateBitmapSourceFromHBitmap drops the alpha channel (icons get a black square around them),
    /// so copy the premultiplied pixels of the DIB section ourselves.
    /// </summary>
    static BitmapSource ToBitmapSource(IntPtr hbitmap)
    {
        BitmapSource image;
        if (GetObject(hbitmap, Marshal.SizeOf<DIBSECTION>(), out var dib) != 0 && dib.dsBm.bmBitsPixel == 32 && dib.dsBm.bmBits != IntPtr.Zero)
        {
            int w = dib.dsBm.bmWidth, h = Math.Abs(dib.dsBm.bmHeight), stride = dib.dsBm.bmWidthBytes;
            var pixels = new byte[stride * h];
            bool bottomUp = dib.biHeight > 0;
            for (int y = 0; y < h; y++)
                Marshal.Copy(dib.dsBm.bmBits + (bottomUp ? h - 1 - y : y) * stride, pixels, y * stride, stride);

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
            image = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, pixels, stride);
        }
        else image = Imaging.CreateBitmapSourceFromHBitmap(hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        return image;
    }

    /// <summary>Opens a program, shortcut, Start menu app (<see cref="AppTarget"/>), URL or protocol link.</summary>
    public static void Open(string target)
    {
        // The click went to us, so we may hand the foreground to whatever opens; otherwise Windows
        // would only flash it in the taskbar, because the notch itself never takes focus.
        AllowSetForegroundWindow(-1 /* ASFW_ANY */);
        ProcessStartInfo start;
        if (target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase))
        {
            start = new ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(target); // quoted if the id is a path with spaces
        }
        else start = new ProcessStartInfo(target) { UseShellExecute = true, WorkingDirectory = WorkingDirectoryOf(target) };
        Process.Start(start)?.Dispose();
    }

    static string WorkingDirectoryOf(string target)
    {
        try { return Path.IsPathFullyQualified(target) ? Path.GetDirectoryName(target) ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>
    /// Runs shell work on a background STA thread: the shell's folders expect a single-threaded
    /// apartment, and walking the app list on the UI thread would stall the notch.
    /// </summary>
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        }) { IsBackground = true, Name = "WinNotch shell" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
