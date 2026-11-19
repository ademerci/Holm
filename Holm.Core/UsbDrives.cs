using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNotch;

/// <summary>A removable drive (USB stick, SD card) as the notch shows it.</summary>
internal sealed record UsbDrive(char Letter, string Label, long Total, long Free)
{
    public string Root => $"{Letter}:\\";
    public string Name => $"{Label} ({Letter}:)";
    public string Detail => UsbDrives.Describe(Total, Free);
}

/// <summary>Removable drives: which letters a device message names, what they are, and ejecting them safely.</summary>
internal static class UsbDrives
{
    /// <summary>The drive letters in a DEV_BROADCAST_VOLUME's unit mask (bit 0 is A:).</summary>
    public static List<char> Letters(uint unitMask)
    {
        var letters = new List<char>();
        for (int bit = 0; bit < 26; bit++)
            if ((unitMask & (1u << bit)) != 0) letters.Add((char)('A' + bit));
        return letters;
    }

    /// <summary>"29.8 GB · 12 GB free", or just the size when it's full or unknown.</summary>
    public static string Describe(long total, long free)
    {
        if (total <= 0) return "Removable drive";
        string size = Size(total);
        return free > 0 ? $"{size} · {Size(free)} free" : size;
    }

    static string Size(long bytes)
    {
        double gb = bytes / (1024.0 * 1024 * 1024);
        if (gb < 1) return $"{bytes / (1024.0 * 1024):0} MB";
        return gb.ToString(gb < 100 ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>The drive if it is a ready removable one, else null.</summary>
    public static UsbDrive? Read(char letter)
    {
        try
        {
            var drive = new DriveInfo(letter + ":\\");
            if (drive.DriveType != DriveType.Removable || !drive.IsReady) return null;
            string label = drive.VolumeLabel.Trim();
            return new UsbDrive(letter, label.Length > 0 ? label : "USB drive", drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static List<UsbDrive> Present()
    {
        var drives = new List<UsbDrive>();
        foreach (var letter in Letters(GetLogicalDrives()))
            if (letter > 'B' && Read(letter) is { } drive) drives.Add(drive);
        return drives;
    }

    // ───────────── safely remove ─────────────

    const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080, DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10, CR_SUCCESS = 0;
    static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b"); // GUID_DEVINTERFACE_DISK

    [StructLayout(LayoutKind.Sequential)] struct STORAGE_DEVICE_NUMBER { public uint DeviceType, DeviceNumber, PartitionNumber; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [DllImport("kernel32.dll")] static extern uint GetLogicalDrives();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr device, uint code, IntPtr input, int inputSize, out STORAGE_DEVICE_NUMBER output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid classGuid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int detailSize, out int required, ref SP_DEVINFO_DATA info);
    [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent, uint devInst, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Request_Device_Eject(uint devInst, out int vetoType, StringBuilder vetoName, int nameLength, uint flags);

    /// <summary>
    /// Ejects the USB device behind a drive letter the way "Safely Remove Hardware" does. Null when done, else
    /// why not (a program still uses it, say). Blocks for a moment: call it off the UI thread.
    /// </summary>
    public static string? Eject(char letter)
    {
        // Never anything but a removable drive: the parent of an internal disk is its controller.
        if (Read(letter) == null) return "Not a removable drive";
        if (DeviceNumber($@"\\.\{letter}:") is not { } volume) return "Can't find the drive";
        if (DiskOf(volume) is not { } disk) return "Can't find the device";
        if (CM_Get_Parent(out uint device, disk, 0) != CR_SUCCESS) return "Can't find the device";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var veto = new StringBuilder(260);
            uint result = CM_Request_Device_Eject(device, out int vetoType, veto, veto.Capacity, 0);
            if (result == CR_SUCCESS && vetoType == 0) return null;
            System.Threading.Thread.Sleep(500); // Explorer often lets go of it a moment later
        }
        return "It's still in use – close what's open on it";
    }

    static STORAGE_DEVICE_NUMBER? DeviceNumber(string path)
    {
        // No access rights needed for this question, so it works without admin and with files open.
        IntPtr handle = CreateFile(path, 0, 3 /* FILE_SHARE_READ | WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
        if (handle == new IntPtr(-1)) return null;
        try
        {
            return DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, IntPtr.Zero, 0, out var number, Marshal.SizeOf<STORAGE_DEVICE_NUMBER>(), out _, IntPtr.Zero)
                ? number : null;
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>The device instance of the disk the volume is on.</summary>
    static uint? DiskOf(STORAGE_DEVICE_NUMBER volume)
    {
        var guid = DiskInterface;
        IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return null;
        IntPtr detail = Marshal.AllocHGlobal(2048);
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) return null;
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, 2048, out _, ref info)) continue;
                string? path = Marshal.PtrToStringUni(detail + 4);
                if (path == null || DeviceNumber(path) is not { } disk) continue;
                if (disk.DeviceType == volume.DeviceType && disk.DeviceNumber == volume.DeviceNumber) return info.DevInst;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(detail);
            SetupDiDestroyDeviceInfoList(set);
        }
    }
}
