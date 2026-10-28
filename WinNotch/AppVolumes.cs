using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static WinNotch.VolumeService;

namespace WinNotch;

/// <summary>
/// The volume of each app on the default playback device (Core Audio's IAudioSessionManager2). Windows reports new
/// sessions and every session's volume and state changes, so the mixer follows without polling. Call it off the UI
/// thread: it lives in the multithreaded apartment, where the session callbacks are dependable.
/// </summary>
internal sealed class AppVolumes : IDisposable
{
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IAudioSessionControl2 control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out ISimpleAudioVolume volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
        [PreserveSig] int RegisterSessionNotification(IAudioSessionNotification notification);
        [PreserveSig] int UnregisterSessionNotification(IAudioSessionNotification notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    // IAudioSessionControl and the methods IAudioSessionControl2 adds, in table order.
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IAudioSessionEvents events);
        [PreserveSig] int UnregisterAudioSessionNotification(IAudioSessionEvents events);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionNotification
    {
        [PreserveSig] int OnSessionCreated(IntPtr newSession);
    }

    [ComImport, Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEvents
    {
        [PreserveSig] int OnDisplayNameChanged([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int OnIconPathChanged([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int OnSimpleVolumeChanged(float level, [MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        [PreserveSig] int OnChannelVolumeChanged(uint count, IntPtr levels, uint changed, ref Guid context);
        [PreserveSig] int OnGroupingParamChanged(ref Guid grouping, ref Guid context);
        [PreserveSig] int OnStateChanged(int state);
        [PreserveSig] int OnSessionDisconnected(int reason);
    }

    /// <summary>Receives Windows' calls, on audio worker threads; it only says that something changed.</summary>
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Listener : IAudioSessionNotification, IAudioSessionEvents
    {
        readonly AppVolumes _owner;
        public Listener(AppVolumes owner) => _owner = owner;
        public int OnSessionCreated(IntPtr newSession) { _owner.Changed?.Invoke(); return 0; }
        public int OnDisplayNameChanged(string name, ref Guid context) => 0;
        public int OnIconPathChanged(string path, ref Guid context) => 0;
        public int OnSimpleVolumeChanged(float level, bool muted, ref Guid context)
        {
            if (context != _owner._context) _owner.Changed?.Invoke(); // not the ones we make by dragging
            return 0;
        }
        public int OnChannelVolumeChanged(uint count, IntPtr levels, uint changed, ref Guid context) => 0;
        public int OnGroupingParamChanged(ref Guid grouping, ref Guid context) => 0;
        public int OnStateChanged(int state) { _owner.Changed?.Invoke(); return 0; }
        public int OnSessionDisconnected(int reason) { _owner.Changed?.Invoke(); return 0; }
    }

    readonly object _gate = new();
    readonly Listener _listener;
    Guid _context = Guid.NewGuid();
    IAudioSessionManager2? _manager;
    readonly List<(IAudioSessionControl2 Control, ISimpleAudioVolume Volume, string Key)> _sessions = new();

    /// <summary>Raised on an audio thread when sessions come, go, or change volume elsewhere.</summary>
    public event Action? Changed;

    public AppVolumes(string? captureDeviceId = null)
    {
        _listener = new Listener(this);
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            IMMDevice device;
            int result = captureDeviceId == null
                ? enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device)
                : enumerator.GetDevice(captureDeviceId, out device);
            if (result != 0) throw new COMException("Audio device unavailable", result);
            var iid = typeof(IAudioSessionManager2).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var manager));
            Marshal.ReleaseComObject(device);
            _manager = (IAudioSessionManager2)manager;
            // Windows only reports new sessions to someone who has listed them once.
            Marshal.ThrowExceptionForHR(_manager.GetSessionEnumerator(out var sessions));
            Marshal.ReleaseComObject(sessions);
            _manager.RegisterSessionNotification(_listener);
        }
        finally { Marshal.ReleaseComObject(enumerator); }
    }

    /// <summary>The apps with sound on the default device, as the mixer shows them.</summary>
    public List<AppVolume> Read()
        => AppVolumeList.Build(ReadSessions(), Environment.ProcessId);

    internal List<AudioSessionInfo> ReadSessions()
    {
        lock (_gate)
        {
            Forget();
            var infos = new List<AudioSessionInfo>();
            if (_manager == null || _manager.GetSessionEnumerator(out var list) != 0) return new();
            try
            {
                list.GetCount(out int count);
                for (int i = 0; i < count; i++)
                {
                    if (list.GetSession(i, out var control) != 0) continue;
                    if (control is not ISimpleAudioVolume volume) { Marshal.ReleaseComObject(control); continue; }
                    control.GetState(out int state);
                    control.GetProcessId(out uint pid);
                    control.GetDisplayName(out string display);
                    volume.GetMasterVolume(out float level);
                    volume.GetMute(out bool muted);
                    bool system = control.IsSystemSoundsSession() == 0;
                    string exe = system ? "" : Native.ProcessPath((int)pid);
                    var info = new AudioSessionInfo((int)pid, exe, NameOf(display, exe), state, level, muted, system);
                    infos.Add(info);
                    _sessions.Add((control, volume, AppVolumeList.KeyOf(info)));
                    control.RegisterAudioSessionNotification(_listener);
                }
            }
            finally { Marshal.ReleaseComObject(list); }
            return infos;
        }
    }

    /// <summary>What the app calls its sound ("@%SystemRoot%…" are resource names, not names), else its program's description.</summary>
    static string NameOf(string? display, string exe)
    {
        if (!string.IsNullOrWhiteSpace(display) && !display.StartsWith('@')) return display.Trim();
        if (exe.Length == 0) return "";
        try { return FileVersionInfo.GetVersionInfo(exe).FileDescription?.Trim() ?? ""; }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException) { return ""; }
    }

    public void SetVolume(string key, float level)
    {
        lock (_gate)
            foreach (var session in _sessions)
                if (session.Key == key) session.Volume.SetMasterVolume(Math.Clamp(level, 0, 1), ref _context);
    }

    public void SetMute(string key, bool mute)
    {
        lock (_gate)
            foreach (var session in _sessions)
                if (session.Key == key) session.Volume.SetMute(mute, ref _context);
    }

    void Forget()
    {
        foreach (var (control, _, _) in _sessions)
        {
            try { control.UnregisterAudioSessionNotification(_listener); } catch (COMException) { }
            Marshal.ReleaseComObject(control);
        }
        _sessions.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Forget();
            if (_manager == null) return;
            try { _manager.UnregisterSessionNotification(_listener); } catch (COMException) { }
            Marshal.ReleaseComObject(_manager);
            _manager = null;
        }
    }
}
