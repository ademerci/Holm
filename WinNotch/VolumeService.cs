using System;
using System.Runtime.InteropServices;

namespace WinNotch;

/// <summary>Master volume of the default playback device (Core Audio). Only bound while the notch is open.</summary>
internal sealed class VolumeService
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpoint);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
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

    IAudioEndpointVolume? _endpoint;
    Guid _context = Guid.NewGuid();

    public void Bind()
    {
        Release();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out var device) != 0) return;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var endpoint) == 0)
                _endpoint = (IAudioEndpointVolume)endpoint;
        }
        catch { _endpoint = null; }
    }

    public void Release()
    {
        if (_endpoint != null) { try { Marshal.ReleaseComObject(_endpoint); } catch { } }
        _endpoint = null;
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
