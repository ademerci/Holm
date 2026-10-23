using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WinNotch;

[ComImport, Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolumeCallback
{
    [PreserveSig] int OnNotify(IntPtr notifyData);
}

[StructLayout(LayoutKind.Sequential)]
struct PROPERTYKEY { public Guid fmtid; public int pid; }

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
}

/// <summary>A playback device that is plugged in and turned on.</summary>
/// <param name="FormFactor">EndpointFormFactor: 3 headphones, 5 headset, 9 a display (HDMI), 1 speakers, …</param>
public sealed record OutputDevice(string Id, string Name, int FormFactor)
{
    /// <summary>Segoe Fluent Icons glyph for the kind of device.</summary>
    public string Glyph => FormFactor switch
    {
        3 => "",     // headphones
        5 or 6 => "", // headset, handset
        9 => "",     // a monitor or TV over HDMI / DisplayPort
        _ => "",     // speakers and everything else
    };
}

/// <summary>
/// Master volume of the default playback device (Core Audio). Windows calls us when the volume
/// changes or another output device becomes the default, so nothing is polled.
/// </summary>
internal sealed class VolumeService
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    /// <summary>Only the parts we read: the type, and a string pointer or a number in the first field.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPVARIANT
    {
        public ushort vt;
        ushort r1, r2, r3;
        public IntPtr value;
        IntPtr more;
    }

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PROPVARIANT value);

    // Not documented, but what the Sound settings themselves use to change the default device (Windows 7 to 11).
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        // Only the place in the table matters for the ones we never call.
        [PreserveSig] int GetMixFormat();
        [PreserveSig] int GetDeviceFormat();
        [PreserveSig] int ResetDeviceFormat();
        [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod();
        [PreserveSig] int SetProcessingPeriod();
        [PreserveSig] int GetShareMode();
        [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue();
        [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    static PROPERTYKEY FriendlyNameKey = new() { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 };
    static PROPERTYKEY FormFactorKey = new() { fmtid = new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"), pid = 0 };

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    /// <summary>Receives Windows' calls. They arrive on audio worker threads.</summary>
    [ComVisible(true)]
    internal sealed class Listener : IAudioEndpointVolumeCallback, IMMNotificationClient
    {
        readonly VolumeService _owner;
        public Listener(VolumeService owner) => _owner = owner;

        public int OnNotify(IntPtr data)
        {
            // AUDIO_VOLUME_NOTIFICATION_DATA: GUID guidEventContext; BOOL bMuted; float fMasterVolume; ...
            if (data == IntPtr.Zero) return 0;
            var context = Marshal.PtrToStructure<Guid>(data);
            bool muted = Marshal.ReadInt32(data, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, 20));
            if (context != _owner._context) _owner.Changed?.Invoke(level, muted);
            return 0;
        }

        public void OnDefaultDeviceChanged(int flow, int role, string? defaultDeviceId)
        {
            if (flow == 0 /* eRender */ && role == 1 /* eMultimedia */) _owner.DefaultDeviceChanged?.Invoke();
        }

        public void OnDeviceStateChanged(string deviceId, int newState) { }
        public void OnDeviceAdded(string deviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnPropertyValueChanged(string deviceId, PROPERTYKEY key) { }
    }

    readonly Listener _listener;
    IMMDeviceEnumerator? _enumerator;
    IAudioEndpointVolume? _endpoint;
    Guid _context = Guid.NewGuid(); // marks changes we make ourselves, so they don't pop up the notch

    /// <summary>The volume was changed by someone else (keys, Windows, another app). Raised on a worker thread.</summary>
    public event Action<float, bool>? Changed;
    /// <summary>Another output device became the default. Raised on a worker thread; call <see cref="Bind"/> on the UI thread.</summary>
    public event Action? DefaultDeviceChanged;

    public VolumeService() => _listener = new Listener(this);

    /// <summary>Starts listening to the default device's volume. Call again when the default device changes.</summary>
    public void Bind()
    {
        Release();
        try
        {
            if (_enumerator == null)
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                _enumerator.RegisterEndpointNotificationCallback(_listener);
            }
            if (_enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var endpoint) == 0)
            {
                _endpoint = (IAudioEndpointVolume)endpoint;
                _endpoint.RegisterControlChangeNotify(_listener);
            }
            Marshal.ReleaseComObject(device);
        }
        catch (Exception ex)
        {
            Diag.Log("Volume unavailable: " + ex.Message);
            _endpoint = null;
        }
    }

    void Release()
    {
        if (_endpoint == null) return;
        try { _endpoint.UnregisterControlChangeNotify(_listener); } catch { }
        try { Marshal.ReleaseComObject(_endpoint); } catch { }
        _endpoint = null;
    }

    /// <summary>Stops listening entirely (app exit).</summary>
    public void Shutdown()
    {
        Release();
        if (_enumerator == null) return;
        try { _enumerator.UnregisterEndpointNotificationCallback(_listener); } catch { }
        try { Marshal.ReleaseComObject(_enumerator); } catch { }
        _enumerator = null;
    }

    public bool TryGet(out float level, out bool muted)
    {
        level = 0; muted = false;
        try
        {
            return _endpoint != null
                && _endpoint.GetMasterVolumeLevelScalar(out level) == 0
                && _endpoint.GetMute(out muted) == 0;
        }
        catch { return false; }
    }

    public void SetLevel(float level)
    {
        if (_endpoint == null) return;
        try
        {
            _endpoint.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref _context);
            if (_endpoint.GetMute(out bool muted) == 0 && muted && level > 0) _endpoint.SetMute(false, ref _context);
        }
        catch { }
    }

    public void ToggleMute()
    {
        if (_endpoint == null) return;
        try { if (_endpoint.GetMute(out bool muted) == 0) _endpoint.SetMute(!muted, ref _context); } catch { }
    }

    /// <summary>The playback devices you can switch to, by name, and which one is the default now. Empty if Windows won't say.</summary>
    public IReadOnlyList<OutputDevice> Outputs(out string? defaultId)
    {
        defaultId = null;
        var list = new List<OutputDevice>();
        if (_enumerator == null) return list;
        IMMDeviceCollection? devices = null;
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var current) == 0)
            {
                if (current.GetId(out string id) == 0) defaultId = id;
                Marshal.ReleaseComObject(current);
            }
            if (_enumerator.EnumAudioEndpoints(0 /* eRender */, 1 /* DEVICE_STATE_ACTIVE */, out devices) != 0 || devices.GetCount(out int count) != 0)
                return list;
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                try
                {
                    if (device.GetId(out string id) == 0)
                    {
                        var (name, formFactor) = Describe(device);
                        list.Add(new OutputDevice(id, name, formFactor));
                    }
                }
                finally { Marshal.ReleaseComObject(device); }
            }
        }
        catch (Exception ex) { Diag.Log("Output devices unavailable: " + ex.Message); }
        finally { if (devices != null) Marshal.ReleaseComObject(devices); }
        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return list;
    }

    /// <summary>The default playback device for a role (0 console, 1 multimedia, 2 communications), or null.</summary>
    internal string? DefaultId(int role)
    {
        if (_enumerator == null || _enumerator.GetDefaultAudioEndpoint(0 /* eRender */, role, out var device) != 0) return null;
        try { return device.GetId(out string id) == 0 ? id : null; }
        finally { Marshal.ReleaseComObject(device); }
    }

    static (string Name, int FormFactor) Describe(IMMDevice device)
    {
        string name = "Unknown device";
        int formFactor = 10; // UnknownFormFactor
        if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) != 0) return (name, formFactor);
        try
        {
            if (store.GetValue(ref FriendlyNameKey, out var value) == 0)
            {
                if (value.vt == 31 /* VT_LPWSTR */ && value.value != IntPtr.Zero) name = Marshal.PtrToStringUni(value.value) ?? name;
                PropVariantClear(ref value);
            }
            if (store.GetValue(ref FormFactorKey, out value) == 0)
            {
                if (value.vt == 19 /* VT_UI4 */) formFactor = unchecked((int)value.value.ToInt64());
                PropVariantClear(ref value);
            }
        }
        finally { Marshal.ReleaseComObject(store); }
        return (name, formFactor);
    }

    /// <summary>
    /// Makes <paramref name="id"/> the default playback device for everything (music, games and calls), like picking it
    /// in the Sound settings. Windows then raises <see cref="DefaultDeviceChanged"/>.
    /// </summary>
    public bool SetDefault(string id) => SetDefault(id, 0, 1, 2); // eConsole, eMultimedia, eCommunications

    internal bool SetDefault(string id, params int[] roles)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (int role in roles)
                    if (policy.SetDefaultEndpoint(id, role) != 0) return false;
                return true;
            }
            finally { Marshal.ReleaseComObject(policy); }
        }
        catch (Exception ex)
        {
            Diag.Log("Couldn't change the output device: " + ex.Message);
            return false;
        }
    }
}
