using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Radios;

namespace WinNotch;

/// <summary>A paired device in the Bluetooth list, and whether the notch can connect and disconnect it.</summary>
internal sealed record BtEntry(BtDevice Device, bool CanConnect);

/// <summary>
/// Switches Bluetooth on and off and connects or disconnects paired headphones and speakers, like the
/// buttons in Windows' Bluetooth settings. Mice, keyboards and phones connect by themselves: Windows
/// gives apps no way to do that for them.
/// </summary>
internal static class BluetoothControl
{
    // Core Audio's map of a sound device: the endpoint (what the Sound settings list) is wired to the
    // driver's filter, and that filter answers the Bluetooth audio driver's properties.
    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        // Only the start of the table is declared: nothing after it is called.
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector other);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector other);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPart
    {
        // Only the place in the table matters for the ones we never call.
        [PreserveSig] int GetName();
        [PreserveSig] int GetLocalId();
        [PreserveSig] int GetGlobalId();
        [PreserveSig] int GetPartType();
        [PreserveSig] int GetSubType();
        [PreserveSig] int GetControlInterfaceCount();
        [PreserveSig] int GetControlInterface();
        [PreserveSig] int EnumPartsIncoming();
        [PreserveSig] int EnumPartsOutgoing();
        [PreserveSig] int GetTopologyObject();
        [PreserveSig] int Activate(int clsContext, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KSPROPERTY property, int propertyLength, IntPtr data, int dataLength, out int bytesReturned);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KSPROPERTY { public Guid Set; public uint Id; public uint Flags; }

    // KSPROPSETID_BtAudio from bthdef.h: "connect again" and "disconnect", what the Bluetooth settings page sends.
    static readonly Guid BtAudioProperties = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    const uint OneShotReconnect = 0, OneShotDisconnect = 1, KsPropertyTypeGet = 1;
    const int ClsCtxAll = 0x17;

    static PROPERTYKEY ContainerIdKey = new() { fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), pid = 2 };

    /// <summary>The PC's Bluetooth radio, or null when it has none (or Windows won't say).</summary>
    public static async Task<Radio?> RadioAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            return radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch (Exception ex)
        {
            Diag.Log("Bluetooth radio unavailable: " + ex.Message);
            return null;
        }
    }

    /// <summary>Switches Bluetooth on or off, like the switch in Settings. False if Windows didn't let us.</summary>
    public static async Task<bool> SetRadioAsync(Radio radio, bool on)
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed) return false;
            return await radio.SetStateAsync(on ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            Diag.Log("Couldn't switch Bluetooth: " + ex.Message);
            return false;
        }
    }

    /// <summary>The paired devices by name, each with whether it is a sound device we can connect. Run it off the UI thread.</summary>
    public static List<BtEntry> Paired()
    {
        var filters = new List<(string Id, Guid Container)>();
        try { ForEachFilter((id, container, _) => filters.Add((id, container))); }
        catch (Exception ex) { Diag.Log("Bluetooth sound devices unavailable: " + ex.Message); }
        return BluetoothBattery.ScanAll()
            .Where(d => d.Paired)
            .Select(d => new BtEntry(d, filters.Any(f => Belongs(f.Id, f.Container, d))))
            .ToList();
    }

    /// <summary>
    /// Asks Windows to connect (or disconnect) all of the device's sound parts, music and calls. True when the
    /// driver took the request; the device itself connects a moment later, if it's on and in reach.
    /// </summary>
    public static bool SetConnected(BtDevice device, bool connect)
    {
        int asked = 0;
        try
        {
            ForEachFilter((id, container, part) =>
            {
                if (!Belongs(id, container, device)) return;
                object? control = null;
                try
                {
                    Guid iid = typeof(IKsControl).GUID;
                    if (part.Activate(ClsCtxAll, ref iid, out control) != 0) return;
                    var property = new KSPROPERTY { Set = BtAudioProperties, Id = connect ? OneShotReconnect : OneShotDisconnect, Flags = KsPropertyTypeGet };
                    int hr = ((IKsControl)control).KsProperty(ref property, Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
                    if (hr == 0) asked++;
                    else Diag.Log($"Bluetooth {(connect ? "connect" : "disconnect")} refused: 0x{hr:X8}");
                }
                finally { Release(control); }
            });
        }
        catch (Exception ex) { Diag.Log("Couldn't reach the Bluetooth sound driver: " + ex.Message); }
        return asked > 0;
    }

    /// <summary>
    /// Whether a sound driver filter is one of the device's: Bluetooth music filters carry the device's address in
    /// their id, call (hands-free) filters don't, but all of them share the device's container.
    /// </summary>
    internal static bool Belongs(string filterId, Guid container, BtDevice device)
        => (device.Address.Length == 12 && filterId.Contains(device.Address, StringComparison.OrdinalIgnoreCase))
        || (container != Guid.Empty && container == device.ContainerId);

    /// <summary>Bluetooth classic and hands-free filters ("{2}.\\?\bthenum#…", "{2}.\\?\bthhfenum#…").</summary>
    internal static bool IsBluetooth(string filterId) => filterId.Contains(@"\\?\bth", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Visits each Bluetooth sound driver filter once, whether its device is connected or not, with its id, its
    /// container and the part to talk to. The part is released afterwards.
    /// </summary>
    static void ForEachFilter(Action<string, Guid, IPart> visit)
    {
        var enumerator = (VolumeService.IMMDeviceEnumerator)new VolumeService.MMDeviceEnumerator();
        VolumeService.IMMDeviceCollection? devices = null;
        try
        {
            if (enumerator.EnumAudioEndpoints(2 /* eAll */, 0xF /* DEVICE_STATEMASK_ALL */, out devices) != 0 || devices.GetCount(out int count) != 0)
                return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                object? topology = null;
                IConnector? connector = null, other = null;
                try
                {
                    Guid iid = typeof(IDeviceTopology).GUID;
                    if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out topology) != 0) continue;
                    if (((IDeviceTopology)topology).GetConnector(0, out connector) != 0) continue;
                    if (connector.GetDeviceIdConnectedTo(out string filterId) != 0 || !IsBluetooth(filterId) || !seen.Add(filterId)) continue;
                    if (connector.GetConnectedTo(out other) != 0) continue;
                    visit(filterId, ContainerOf(device), (IPart)other);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException) { }
                finally
                {
                    Release(other);
                    Release(connector);
                    Release(topology);
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        finally
        {
            Release(devices);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    static Guid ContainerOf(VolumeService.IMMDevice device)
    {
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return Guid.Empty;
        try
        {
            if (store.GetValue(ref ContainerIdKey, out var value) != 0) return Guid.Empty;
            try { return value.vt == 72 /* VT_CLSID */ && value.value != IntPtr.Zero ? Marshal.PtrToStructure<Guid>(value.value) : Guid.Empty; }
            finally { VolumeService.PropVariantClear(ref value); }
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    static void Release(object? com)
    {
        if (com != null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }
}
