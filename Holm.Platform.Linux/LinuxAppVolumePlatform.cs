using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Linux;

/// <summary>
/// The volume of each app through PulseAudio (and PipeWire's PulseAudio server): every sound stream an app plays
/// is a sink input, with the app's name, program and icon name attached. They are grouped into apps the way the
/// WPF app groups Windows' sessions (AppVolumeList, linked). A connection of its own, open only while the mixer
/// page is: the server announces streams coming, going and changing, nothing is polled.
/// </summary>
public sealed class LinuxAppVolumePlatform : IAppVolumePlatform
{
    const int ContextReady = 4, ContextFailed = 5, ContextTerminated = 6;
    const uint SubscribeSinkInput = 0x0004;
    const uint VolumeNorm = 0x10000;
    // Where the fields this needs sit in pa_sink_input_info (64-bit; part of the library's ABI since PulseAudio 1.0).
    const int InputIndex = 0, InputVolumeChannels = 172, InputVolumeValues = 176, InputMute = 336, InputProplist = 344, InputCorked = 352;

    readonly ContextCallback _onState;
    readonly SuccessCallback _onWrite;
    readonly SubscribeCallback _onEvent;
    readonly InputCallback _onInput;
    readonly object _gate = new();
    IntPtr _loop, _context;
    bool _unavailable;
    // Filled while a list is being read, under the library's lock.
    List<(uint Index, AudioSessionInfo Info, string Icon, byte Channels)>? _reading;
    bool _readDone;
    // The streams of the last read, by app.
    Dictionary<string, List<(uint Index, byte Channels)>> _streams = new();
    Dictionary<string, (string Icon, string Binary)> _icons = new();

    public bool Supported => true;
    public event Action? Changed;

    public LinuxAppVolumePlatform()
    {
        _onState = OnState;
        _onWrite = (_, _, _) => pa_threaded_mainloop_signal(_loop, 0);
        _onEvent = OnEvent;
        _onInput = OnInput;
    }

    public void Start()
    {
        if (_unavailable || _loop != IntPtr.Zero) return;
        try
        {
            _loop = pa_threaded_mainloop_new();
            if (_loop == IntPtr.Zero) { _unavailable = true; return; }
            if (pa_threaded_mainloop_start(_loop) < 0)
            {
                pa_threaded_mainloop_free(_loop);
                _loop = IntPtr.Zero;
                _unavailable = true;
                return;
            }
            pa_threaded_mainloop_lock(_loop);
            try
            {
                _context = pa_context_new(pa_threaded_mainloop_get_api(_loop), "Holm mixer");
                if (_context == IntPtr.Zero) return;
                pa_context_set_state_callback(_context, _onState, IntPtr.Zero);
                if (pa_context_connect(_context, null, 0, IntPtr.Zero) < 0) return;
                // Wait until the connection is up or has failed, so the first read finds it ready.
                while (true)
                {
                    int state = pa_context_get_state(_context);
                    if (state is ContextReady or ContextFailed or ContextTerminated) break;
                    pa_threaded_mainloop_wait(_loop);
                }
            }
            finally { pa_threaded_mainloop_unlock(_loop); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _unavailable = true; }
    }

    public void Stop()
    {
        if (_loop == IntPtr.Zero) return;
        pa_threaded_mainloop_lock(_loop);
        if (_context != IntPtr.Zero)
        {
            pa_context_set_state_callback(_context, null, IntPtr.Zero);
            pa_context_set_subscribe_callback(_context, null, IntPtr.Zero);
            pa_context_disconnect(_context);
            pa_context_unref(_context);
            _context = IntPtr.Zero;
        }
        pa_threaded_mainloop_unlock(_loop);
        pa_threaded_mainloop_stop(_loop);
        pa_threaded_mainloop_free(_loop);
        _loop = IntPtr.Zero;
    }

    void OnState(IntPtr context, IntPtr userdata)
    {
        try
        {
            if (pa_context_get_state(context) == ContextReady)
            {
                pa_context_set_subscribe_callback(context, _onEvent, IntPtr.Zero);
                Unref(pa_context_subscribe(context, SubscribeSinkInput, IntPtr.Zero, IntPtr.Zero));
            }
            pa_threaded_mainloop_signal(_loop, 0);
        }
        catch { }
    }

    void OnEvent(IntPtr context, int type, uint index, IntPtr userdata)
    {
        try { Changed?.Invoke(); } catch { }
    }

    public List<AppVolumeEntry> Read()
    {
        if (_loop == IntPtr.Zero) return new();
        List<(uint Index, AudioSessionInfo Info, string Icon, byte Channels)> streams;
        pa_threaded_mainloop_lock(_loop);
        try
        {
            if (_context == IntPtr.Zero || pa_context_get_state(_context) != ContextReady) return new();
            _reading = new();
            _readDone = false;
            IntPtr operation = pa_context_get_sink_input_info_list(_context, _onInput, IntPtr.Zero);
            if (operation == IntPtr.Zero) return new();
            // OnState wakes this too, should the connection go while the list is read.
            while (!_readDone && pa_context_get_state(_context) == ContextReady) pa_threaded_mainloop_wait(_loop);
            pa_operation_unref(operation);
            streams = _reading;
            _reading = null;
        }
        finally { pa_threaded_mainloop_unlock(_loop); }

        var apps = AppVolumeList.Build(streams.Select(s => s.Info).ToList(), Environment.ProcessId);
        lock (_gate)
        {
            _streams = apps.ToDictionary(a => a.Key, a => a.Sessions.Select(i => (streams[i].Index, streams[i].Channels)).ToList());
            _icons = apps.ToDictionary(a => a.Key, a => (streams[a.Sessions[0]].Icon, a.ExePath));
        }
        return apps.Select(a => new AppVolumeEntry(a.Key, a.Name, a.Volume, a.Muted, a.Playing)).ToList();
    }

    void OnInput(IntPtr context, IntPtr info, int end, IntPtr userdata)
    {
        try
        {
            if (end != 0 || info == IntPtr.Zero) { _readDone = true; pa_threaded_mainloop_signal(_loop, 0); return; }
            uint index = (uint)Marshal.ReadInt32(info, InputIndex);
            int channels = Math.Clamp(Marshal.ReadByte(info, InputVolumeChannels), (byte)1, (byte)32);
            ulong sum = 0;
            for (int i = 0; i < channels; i++) sum += (uint)Marshal.ReadInt32(info, InputVolumeValues + 4 * i);
            float volume = Math.Clamp((float)(sum / (ulong)channels) / VolumeNorm, 0f, 1f);
            bool muted = Marshal.ReadInt32(info, InputMute) != 0;
            bool corked = Marshal.ReadInt32(info, InputCorked) != 0;
            IntPtr props = Marshal.ReadIntPtr(info, InputProplist);
            string Prop(string key) => props == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pa_proplist_gets(props, key)) ?? "";
            _reading?.Add((index, Session(Prop("application.name"), Prop("application.process.binary"), Prop("application.process.id"),
                Prop("media.role"), volume, muted, corked), Prop("application.icon_name"), (byte)channels));
        }
        catch { }
    }

    /// <summary>A sink input as the WPF app's session: event sounds count as the system's sounds.</summary>
    internal static AudioSessionInfo Session(string name, string binary, string pid, string role, float volume, bool muted, bool corked)
        => new(int.TryParse(pid, out int id) ? id : 0, binary, name, corked ? AudioSessionInfo.Inactive : AudioSessionInfo.Active,
               volume, muted, role == "event");

    public void SetVolume(string key, float level)
    {
        uint raw = (uint)Math.Round(Math.Clamp(level, 0f, 1f) * VolumeNorm);
        foreach (var (index, channels) in StreamsOf(key))
            Locked(() =>
            {
                var volume = new CVolume { Channels = channels };
                unsafe { for (int i = 0; i < channels; i++) volume.Values[i] = raw; }
                WaitForWrite(pa_context_set_sink_input_volume(_context, index, ref volume, _onWrite, IntPtr.Zero));
            });
    }

    public void SetMute(string key, bool mute)
    {
        foreach (var (index, _) in StreamsOf(key))
            Locked(() => WaitForWrite(pa_context_set_sink_input_mute(_context, index, mute ? 1 : 0, _onWrite, IntPtr.Zero)));
    }

    List<(uint Index, byte Channels)> StreamsOf(string key)
    {
        lock (_gate) return _streams.TryGetValue(key, out var list) ? list.ToList() : new();
    }

    void Locked(Action action)
    {
        if (_loop == IntPtr.Zero) return;
        pa_threaded_mainloop_lock(_loop);
        try { if (_context != IntPtr.Zero && pa_context_get_state(_context) == ContextReady) action(); }
        finally { pa_threaded_mainloop_unlock(_loop); }
    }

    // Wait for the server's reply so closing the mixer cannot disconnect before the write is applied.
    void WaitForWrite(IntPtr operation)
    {
        const int Running = 0; // PA_OPERATION_RUNNING
        if (operation == IntPtr.Zero) return;
        try
        {
            while (pa_operation_get_state(operation) == Running && pa_context_get_state(_context) == ContextReady)
                pa_threaded_mainloop_wait(_loop);
        }
        finally { pa_operation_unref(operation); }
    }

    public AppIcon? IconOf(string key)
    {
        (string Icon, string Binary) names;
        lock (_gate) if (!_icons.TryGetValue(key, out names)) return null;
        return (FindIcon(names.Icon) ?? FindIcon(names.Binary)) is { } file ? new AppIcon(file, null) : null;
    }

    static readonly string[] IconRoots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/icons/hicolor"),
        "/usr/share/icons/hicolor", "/usr/local/share/icons/hicolor", "/var/lib/flatpak/exports/share/icons/hicolor",
        "/var/lib/snapd/desktop/icons",
    };

    static readonly string[] IconSizes = { "48x48", "64x64", "32x32", "128x128", "256x256", "96x96", "24x24" };

    /// <summary>An icon name as the freedesktop icon theme files it, as a PNG of a size that suits a 22 px icon.</summary>
    internal static string? FindIcon(string name, IEnumerable<string>? roots = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('/') || name.Contains("..")) return null;
        foreach (string root in roots ?? IconRoots)
            foreach (string size in IconSizes)
            {
                string file = Path.Combine(root, size, "apps", name + ".png");
                if (File.Exists(file)) return file;
            }
        string pixmap = Path.Combine("/usr/share/pixmaps", name + ".png");
        return roots == null && File.Exists(pixmap) ? pixmap : null;
    }

    static void Unref(IntPtr operation)
    {
        if (operation != IntPtr.Zero) pa_operation_unref(operation);
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct CVolume { public byte Channels; public fixed uint Values[32]; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ContextCallback(IntPtr context, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SuccessCallback(IntPtr context, int success, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SubscribeCallback(IntPtr context, int type, uint index, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InputCallback(IntPtr context, IntPtr info, int end, IntPtr userdata);

    const string Pulse = "libpulse.so.0";

    [DllImport(Pulse)] static extern IntPtr pa_threaded_mainloop_new();
    [DllImport(Pulse)] static extern int pa_threaded_mainloop_start(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_stop(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_free(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_lock(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_unlock(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_wait(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_signal(IntPtr loop, int waitForAccept);
    [DllImport(Pulse)] static extern IntPtr pa_threaded_mainloop_get_api(IntPtr loop);
    [DllImport(Pulse)] static extern IntPtr pa_context_new(IntPtr api, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Pulse)] static extern void pa_context_unref(IntPtr context);
    [DllImport(Pulse)] static extern int pa_context_connect(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string? server, int flags, IntPtr spawnApi);
    [DllImport(Pulse)] static extern void pa_context_disconnect(IntPtr context);
    [DllImport(Pulse)] static extern int pa_context_get_state(IntPtr context);
    [DllImport(Pulse)] static extern void pa_context_set_state_callback(IntPtr context, ContextCallback? callback, IntPtr userdata);
    [DllImport(Pulse)] static extern void pa_context_set_subscribe_callback(IntPtr context, SubscribeCallback? callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_subscribe(IntPtr context, uint mask, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_get_sink_input_info_list(IntPtr context, InputCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_sink_input_volume(IntPtr context, uint index, ref CVolume volume, SuccessCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_sink_input_mute(IntPtr context, uint index, int mute, SuccessCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_proplist_gets(IntPtr proplist, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Pulse)] static extern void pa_operation_unref(IntPtr operation);
    [DllImport(Pulse)] static extern int pa_operation_get_state(IntPtr operation);
}
