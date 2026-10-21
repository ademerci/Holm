using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNotch;

internal sealed class BtDevice
{
    public string Address = "";
    public string Name = "";
    public string? FallbackName;
    public int Battery = -1;
    public bool? Connected;
    public uint ClassOfDevice;

    public bool IsConnected => Connected ?? true;

    /// <summary>Segoe Fluent Icons glyph for the kind of device.</summary>
    public string Glyph
    {
        get
        {
            uint major = (ClassOfDevice >> 8) & 0x1F;
            if (major == 0x04) return ""; // audio / video -> headphones
            if (major == 0x05) return ((ClassOfDevice >> 6) & 0x3) == 2 ? "" : ((ClassOfDevice >> 2) & 0xF) == 2 ? "" : "";
            if (major == 0x02) return "";

            var n = Name.ToLowerInvariant();
            if (Regex.IsMatch(n, "buds|pods|head|kopfh|ear|anc|crusher|wh-|wf-|bose|beats|jbl|sony|sennheiser|jabra")) return "";
            if (Regex.IsMatch(n, "mouse|maus|^mx ")) return "";
            if (Regex.IsMatch(n, "keyboard|tastatur|keys")) return "";
            if (Regex.IsMatch(n, "controller|xbox|dualsense|gamepad")) return "";
            if (Regex.IsMatch(n, "pen|stift")) return "";
            return "";
        }
    }
}

/// <summary>
/// Reads the battery level Windows already knows for paired Bluetooth devices (the same value the
/// Settings app shows). This is a plain registry-style lookup, it never talks to the radio.
/// </summary>
internal static class BluetoothBattery
{
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY
    {
        public Guid fmtid; public uint pid;
        public DEVPROPKEY(string guid, uint id) { fmtid = new Guid(guid); pid = id; }
    }

    const uint DIGCF_PRESENT = 0x2, DIGCF_ALLCLASSES = 0x4;
    const uint DEVPROP_TYPE_BYTE = 0x3, DEVPROP_TYPE_UINT32 = 0x7, DEVPROP_TYPE_BOOLEAN = 0x11, DEVPROP_TYPE_STRING = 0x12;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, char[] id, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buffer, int size, out int required, uint flags);

    static DEVPROPKEY KeyBattery = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5", 2);
    static DEVPROPKEY KeyAddress = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 1);
    static DEVPROPKEY KeyClassOfDevice = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A", 10);
    static DEVPROPKEY KeyFriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
    static DEVPROPKEY KeyDeviceDesc = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 2);
    // Not in the public headers, but it is what the Bluetooth settings page uses for "Connected".
    static DEVPROPKEY KeyConnected = new("83DA6326-97A6-4088-9453-A1923F573B29", 15);

    static readonly Regex AddressInId = new(@"(?:DEV_|&|_)([0-9A-F]{12})(?:_|\\|$)", RegexOptions.IgnoreCase);
    static readonly string[] NoiseSuffixes = { " Hands-Free AG", " Hands-Free", " Avrcp Transport", " Stereo" };

    public static List<BtDevice> Scan()
    {
        var byAddress = new Dictionary<string, BtDevice>(StringComparer.OrdinalIgnoreCase);
        var buffer = new byte[1024];
        var idBuffer = new char[512];

        foreach (var enumerator in new[] { "BTHENUM", "BTHLE", "BTHLEDEVICE" })
        {
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) continue;
            try
            {
                var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    string id = SetupDiGetDeviceInstanceIdW(set, ref data, idBuffer, idBuffer.Length, out int len)
                        ? new string(idBuffer, 0, Math.Max(0, len - 1)) : "";
                    string? address = ReadString(set, ref data, ref KeyAddress, buffer);
                    if (address == null && AddressInId.Match(id) is { Success: true } m) address = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(address)) continue;

                    if (!byAddress.TryGetValue(address, out var dev))
                        byAddress[address] = dev = new BtDevice { Address = address.ToUpperInvariant() };

                    bool isDeviceNode = id.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase)
                                     || id.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase);
                    if (isDeviceNode)
                    {
                        dev.Name = ReadString(set, ref data, ref KeyFriendlyName, buffer)
                                ?? ReadString(set, ref data, ref KeyDeviceDesc, buffer) ?? dev.Name;
                        if (ReadBool(set, ref data, ref KeyConnected, buffer) is bool connected) dev.Connected = connected;
                        if (ReadUInt(set, ref data, ref KeyClassOfDevice, buffer) is uint cod) dev.ClassOfDevice = cod;
                    }
                    else if (dev.FallbackName == null && ReadString(set, ref data, ref KeyFriendlyName, buffer) is string n)
                    {
                        dev.FallbackName = Clean(n);
                    }

                    if (ReadByte(set, ref data, ref KeyBattery, buffer) is byte level && level <= 100)
                        dev.Battery = level;
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
        }

        var result = byAddress.Values.Where(d => d.Battery >= 0).ToList();
        foreach (var d in result)
            if (d.Name.Length == 0) d.Name = d.FallbackName ?? "Bluetooth device";
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static string Clean(string name)
    {
        foreach (var suffix in NoiseSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return name;
    }

    static bool Read(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, byte[] buffer, out uint type, out int size)
        => SetupDiGetDevicePropertyW(set, ref data, ref key, out type, buffer, buffer.Length, out size, 0);

    static string? ReadString(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, byte[] buffer)
    {
        if (!Read(set, ref data, ref key, buffer, out uint type, out int size) || type != DEVPROP_TYPE_STRING || size < 2) return null;
        var s = Encoding.Unicode.GetString(buffer, 0, size).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    static byte? ReadByte(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DEVPROP_TYPE_BYTE && size >= 1 ? buffer[0] : null;

    static bool? ReadBool(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DEVPROP_TYPE_BOOLEAN && size >= 1 ? buffer[0] != 0 : null;

    static uint? ReadUInt(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, byte[] buffer)
        => Read(set, ref data, ref key, buffer, out uint type, out int size) && type == DEVPROP_TYPE_UINT32 && size >= 4 ? BitConverter.ToUInt32(buffer, 0) : null;
}
