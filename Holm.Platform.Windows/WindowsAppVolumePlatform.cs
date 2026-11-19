using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// The mixer through the WPF app's AppVolumes (linked): Core Audio's sessions on the default output, followed by
/// their events while the page is open. Icons come from the programs themselves, read without WPF.
/// </summary>
public sealed class WindowsAppVolumePlatform : IAppVolumePlatform
{
    readonly object _gate = new();
    AppVolumes? _volumes;
    Dictionary<string, string> _exeOf = new();

    public bool Supported => true;
    public event Action? Changed;

    public void Start()
    {
        lock (_gate)
        {
            if (_volumes != null) return;
            try
            {
                _volumes = new AppVolumes();
                _volumes.Changed += () => Changed?.Invoke();
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException) { _volumes = null; }
        }
    }

    public void Stop()
    {
        AppVolumes? volumes;
        lock (_gate) { volumes = _volumes; _volumes = null; }
        volumes?.Dispose();
    }

    public List<AppVolumeEntry> Read()
    {
        AppVolumes? volumes;
        lock (_gate) volumes = _volumes;
        if (volumes == null) return new();
        try
        {
            var apps = volumes.Read();
            lock (_gate) _exeOf = apps.ToDictionary(a => a.Key, a => a.ExePath, StringComparer.OrdinalIgnoreCase);
            return apps.Select(a => new AppVolumeEntry(a.Key, a.Name, a.Volume, a.Muted, a.Playing)).ToList();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ObjectDisposedException) { return new(); }
    }

    public void SetVolume(string key, float level)
    {
        try { _volumes?.SetVolume(key, level); } catch (COMException) { }
    }

    public void SetMute(string key, bool mute)
    {
        try { _volumes?.SetMute(key, mute); } catch (COMException) { }
    }

    public AppIcon? IconOf(string key)
    {
        string? exe;
        lock (_gate) _exeOf.TryGetValue(key, out exe);
        return string.IsNullOrEmpty(exe) ? null : IconPixels(exe, 48) is { } pixels ? new AppIcon(null, pixels) : null;
    }

    /// <summary>The program's own icon at <paramref name="size"/> pixels, as straight BGRA.</summary>
    static ScreenImage? IconPixels(string exe, int size)
    {
        IntPtr icon = IntPtr.Zero;
        try
        {
            if (PrivateExtractIconsW(exe, 0, size, size, out icon, out _, 1, 0) != 1 || icon == IntPtr.Zero) return null;
            if (!GetIconInfo(icon, out var info)) return null;
            try
            {
                if (info.Color == IntPtr.Zero) return null;
                var header = new BitmapInfoHeader { Size = Marshal.SizeOf<BitmapInfoHeader>(), Width = size, Height = -size, Planes = 1, BitCount = 32 };
                var bgra = new byte[size * size * 4];
                IntPtr dc = GetDC(IntPtr.Zero);
                try { if (GetDIBits(dc, info.Color, 0, (uint)size, bgra, ref header, 0) == 0) return null; }
                finally { ReleaseDC(IntPtr.Zero, dc); }
                // Old icons have no alpha at all: then the mask says what is see-through.
                bool hasAlpha = false;
                for (int i = 3; i < bgra.Length && !hasAlpha; i += 4) hasAlpha = bgra[i] != 0;
                if (!hasAlpha) for (int i = 3; i < bgra.Length; i += 4) bgra[i] = 0xFF;
                return new ScreenImage(size, size, bgra);
            }
            finally
            {
                if (info.Color != IntPtr.Zero) DeleteObject(info.Color);
                if (info.Mask != IntPtr.Zero) DeleteObject(info.Mask);
            }
        }
        catch { return null; }
        finally { if (icon != IntPtr.Zero) DestroyIcon(icon); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IconInfo { public bool IsIcon; public int HotspotX, HotspotY; public IntPtr Mask, Color; }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfoHeader
    {
        public int Size, Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter; public uint ClrUsed, ClrImportant;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint PrivateExtractIconsW(string file, int index, int cx, int cy, out IntPtr icon, out uint id, uint count, uint flags);
    [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr icon, out IconInfo info);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte[] bits, ref BitmapInfoHeader info, uint usage);
}
