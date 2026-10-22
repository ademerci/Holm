using System;
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

/// <summary>
/// Master volume of the default playback device (Core Audio). Windows calls us when the volume
/// changes or another output device becomes the default, so nothing is polled.
/// </summary>
internal sealed class VolumeService
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

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
}
