using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Holm.Platform;

namespace Holm.Platform.Linux;

/// <summary>
/// Who records: the microphone through PulseAudio (and PipeWire's PulseAudio server), where each app recording is
/// a source output with its name and program attached; the camera through the device files, which the kernel
/// reports opened and closed (inotify), and the processes holding them open. Nothing is polled.
/// </summary>
public sealed class LinuxSensorPlatform : ISensorPlatform
{
    const int ContextReady = 4;
    const uint SubscribeSource = 0x0002, SubscribeSourceOutput = 0x0008, SubscribeServer = 0x0080;
    const uint InvalidIndex = uint.MaxValue;
    // Where the fields this needs sit (64-bit; part of the library's ABI since PulseAudio 1.0), checked against introspect.h.
    const int ServerDefaultSource = 56;
    const int SourceName = 0, SourceIndex = 8, SourceMute = 304, SourceMonitorOf = 308;
    const int OutputSource = 24, OutputProplist = 208, OutputCorked = 216;
    const uint InOpen = 0x20, InCloseWrite = 0x08, InCloseNoWrite = 0x10, InCreate = 0x100, InDelete = 0x200;

    readonly ContextCallback _onState;
    readonly SubscribeCallback _onEvent;
    readonly InfoCallback _onServer;
    readonly ListCallback _onSource, _onOutput;
    readonly object _gate = new();
    IntPtr _loop, _context;
    bool _pulseUnavailable;

    // The read in progress, on the library's thread.
    bool _reading, _readAgain;
    string _defaultSource = "";
    readonly HashSet<uint> _monitors = new();
    uint _defaultIndex = InvalidIndex;
    bool? _readMuted;
    List<(string Name, string Key)> _readMicrophones = new();

    // What was found.
    List<(string Name, string Key)> _microphones = new(), _cameras = new();
    bool? _muted;
    readonly Dictionary<(bool, string), DateTime> _since = new();
    IReadOnlyList<SensorActivity> _inUse = Array.Empty<SensorActivity>();

    Thread? _cameraThread;
    volatile bool _stopping;
    Timer? _cameraSoon;

    public event Action? Changed;

    public LinuxSensorPlatform()
    {
        _onState = OnState;
        _onEvent = (_, _, _, _) => Refresh();
        _onServer = OnServer;
        _onSource = OnSource;
        _onOutput = OnOutput;
    }

    public IReadOnlyList<SensorActivity> InUse { get { lock (_gate) return _inUse; } }

    public void Start()
    {
        _stopping = false;
        StartPulse();
        if (_cameraThread == null)
        {
            _cameraSoon = new Timer(_ => ReadCameras(), null, Timeout.Infinite, Timeout.Infinite);
            _cameraThread = new Thread(WatchCameras) { IsBackground = true, Name = "Holm camera watch" };
            _cameraThread.Start();
        }
    }

    public void Stop()
    {
        _stopping = true;
        _cameraSoon?.Dispose();
        _cameraSoon = null;
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

    // ───────────── microphone ─────────────

    void StartPulse()
    {
        if (_pulseUnavailable || _loop != IntPtr.Zero) return;
        try
        {
            _loop = pa_threaded_mainloop_new();
            if (_loop == IntPtr.Zero || pa_threaded_mainloop_start(_loop) < 0) { _pulseUnavailable = true; return; }
            pa_threaded_mainloop_lock(_loop);
            try
            {
                _context = pa_context_new(pa_threaded_mainloop_get_api(_loop), "Holm privacy");
                if (_context == IntPtr.Zero) return;
                pa_context_set_state_callback(_context, _onState, IntPtr.Zero);
                pa_context_connect(_context, null, 0, IntPtr.Zero);
            }
            finally { pa_threaded_mainloop_unlock(_loop); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { _pulseUnavailable = true; }
    }

    void OnState(IntPtr context, IntPtr userdata)
    {
        try
        {
            if (pa_context_get_state(context) != ContextReady) return;
            pa_context_set_subscribe_callback(context, _onEvent, IntPtr.Zero);
            Unref(pa_context_subscribe(context, SubscribeSource | SubscribeSourceOutput | SubscribeServer, IntPtr.Zero, IntPtr.Zero));
            Refresh();
        }
        catch { }
    }

    /// <summary>On the library's thread: the server (for the default microphone), the sources, then who records.</summary>
    void Refresh()
    {
        if (_context == IntPtr.Zero) return;
        if (_reading) { _readAgain = true; return; }
        _reading = true;
        _readAgain = false;
        _monitors.Clear();
        _readMicrophones = new();
        _readMuted = null;
        _defaultIndex = InvalidIndex;
        IntPtr operation = pa_context_get_server_info(_context, _onServer, IntPtr.Zero);
        if (operation == IntPtr.Zero) _reading = false;
        else Unref(operation);
    }

    void OnServer(IntPtr context, IntPtr info, IntPtr userdata)
    {
        try
        {
            _defaultSource = info == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, ServerDefaultSource)) ?? "";
            IntPtr operation = pa_context_get_source_info_list(context, _onSource, IntPtr.Zero);
            if (operation == IntPtr.Zero) { _reading = false; return; }
            Unref(operation);
        }
        catch { _reading = false; }
    }

    void OnSource(IntPtr context, IntPtr info, int end, IntPtr userdata)
    {
        try
        {
            if (end != 0 || info == IntPtr.Zero)
            {
                IntPtr operation = pa_context_get_source_output_info_list(context, _onOutput, IntPtr.Zero);
                if (operation == IntPtr.Zero) { _reading = false; return; }
                Unref(operation);
                return;
            }
            uint index = (uint)Marshal.ReadInt32(info, SourceIndex);
            // A sink's monitor carries what plays (Holm's equalizer, visualizers): no microphone.
            if ((uint)Marshal.ReadInt32(info, SourceMonitorOf) != InvalidIndex) _monitors.Add(index);
            if (Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, SourceName)) == _defaultSource)
            {
                _defaultIndex = index;
                _readMuted = Marshal.ReadInt32(info, SourceMute) != 0;
            }
        }
        catch { }
    }

    void OnOutput(IntPtr context, IntPtr info, int end, IntPtr userdata)
    {
        try
        {
            if (end != 0 || info == IntPtr.Zero)
            {
                _reading = false;
                lock (_gate)
                {
                    _microphones = _readMicrophones;
                    _muted = _readMuted;
                }
                Publish();
                if (_readAgain) Refresh();
                return;
            }
            uint source = (uint)Marshal.ReadInt32(info, OutputSource);
            if (_monitors.Contains(source) || Marshal.ReadInt32(info, OutputCorked) != 0) return;
            IntPtr props = Marshal.ReadIntPtr(info, OutputProplist);
            string Prop(string key) => props == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pa_proplist_gets(props, key)) ?? "";
            if (int.TryParse(Prop("application.process.id"), out int pid) && pid == Environment.ProcessId) return;
            // Volume meters of mixers watch every source without recording anything worth a dot.
            if (Prop("application.id") == "org.PulseAudio.pavucontrol" || Prop("media.name") == "Peak detect") return;
            string binary = Prop("application.process.binary"), name = Prop("application.name");
            string key = binary.Length > 0 ? binary : name;
            if (key.Length == 0) return;
            _readMicrophones.Add((NameOf(name.Length > 0 ? name : binary), Key(key)));
        }
        catch { }
    }

    public bool TryGetMicrophoneMute(out bool muted)
    {
        lock (_gate)
        {
            muted = _muted == true;
            return _muted.HasValue;
        }
    }

    public void SetMicrophoneMute(bool mute)
    {
        if (_loop == IntPtr.Zero) return;
        pa_threaded_mainloop_lock(_loop);
        try
        {
            if (_context != IntPtr.Zero && _defaultSource.Length > 0)
                Unref(pa_context_set_source_mute_by_name(_context, _defaultSource, mute ? 1 : 0, IntPtr.Zero, IntPtr.Zero));
        }
        finally { pa_threaded_mainloop_unlock(_loop); }
    }

    // ───────────── camera ─────────────

    void WatchCameras()
    {
        int fd = -1;
        try
        {
            fd = inotify_init1(0x80000 /* IN_CLOEXEC */);
            if (fd < 0) return;
            inotify_add_watch(fd, "/dev", InCreate | InDelete);
            foreach (string device in Devices()) inotify_add_watch(fd, device, InOpen | InCloseWrite | InCloseNoWrite);
            ReadCameras();
            var buffer = new byte[4096];
            var poll = new PollFd { Fd = fd, Events = 1 /* POLLIN */ };
            while (!_stopping)
            {
                if (Poll(ref poll, 1, 1000) <= 0) continue;
                int read = (int)Read(fd, buffer, buffer.Length);
                if (read <= 0) continue;
                for (int at = 0; at + 16 <= read;)
                {
                    uint mask = BitConverter.ToUInt32(buffer, at + 4);
                    int length = (int)BitConverter.ToUInt32(buffer, at + 12);
                    string name = length > 0 ? System.Text.Encoding.UTF8.GetString(buffer, at + 16, length).TrimEnd('\0') : "";
                    if ((mask & InCreate) != 0 && name.StartsWith("video", StringComparison.Ordinal))
                        inotify_add_watch(fd, "/dev/" + name, InOpen | InCloseWrite | InCloseNoWrite);
                    at += 16 + length;
                }
                // Opening a camera comes in a burst (formats, controls); look once it has settled.
                _cameraSoon?.Change(250, Timeout.Infinite);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ObjectDisposedException) { }
        finally
        {
            if (fd >= 0) close(fd);
            _cameraThread = null;
        }
    }

    static IEnumerable<string> Devices()
    {
        try { return Directory.GetFiles("/dev", "video*"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>The processes holding a camera open, from their open files (only this user's can be seen).</summary>
    void ReadCameras()
    {
        var found = new List<(string, string)>();
        int own = Environment.ProcessId;
        try
        {
            foreach (string process in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(process), out int pid) || pid == own) continue;
                try
                {
                    foreach (string fd in Directory.EnumerateFiles(Path.Combine(process, "fd")))
                    {
                        if (new FileInfo(fd).LinkTarget is not { } target || !target.StartsWith("/dev/video", StringComparison.Ordinal)) continue;
                        string comm = File.ReadAllText(Path.Combine(process, "comm")).Trim();
                        if (comm.Length > 0 && !found.Any(f => f.Item2 == Key(comm))) found.Add((NameOf(comm), Key(comm)));
                        break;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        lock (_gate) _cameras = found;
        Publish();
    }

    // ───────────── both ─────────────

    void Publish()
    {
        bool changed;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            var current = _cameras.Select(c => (true, c.Name, c.Key)).Concat(_microphones.Select(m => (false, m.Name, m.Key)))
                                  .DistinctBy(u => (u.Item1, u.Key)).ToList();
            foreach (var key in _since.Keys.Where(k => !current.Any(c => (c.Item1, c.Key) == k)).ToList()) _since.Remove(key);
            var list = current.Select(c =>
            {
                if (!_since.TryGetValue((c.Item1, c.Key), out var since)) _since[(c.Item1, c.Key)] = since = now;
                return new SensorActivity(c.Item1, c.Name, c.Key, since);
            }).OrderBy(u => u.Camera ? 0 : 1).ThenBy(u => u.Since).ToList();
            changed = !list.SequenceEqual(_inUse);
            _inUse = list;
            changed |= _muted != _lastMuted;
            _lastMuted = _muted;
        }
        if (changed) try { Changed?.Invoke(); } catch { }
    }

    bool? _lastMuted;

    /// <summary>Program names as the call list knows them ("google-chrome" is Chrome, "zoom" is Zoom).</summary>
    internal static string Key(string program) => Path.GetFileName(program) switch
    {
        "google-chrome" or "google-chrome-stable" or "chrome" => "chrome",
        "chromium" or "chromium-browser" or "chromium-browse" => "chromium",
        "brave" or "brave-browser" => "brave",
        "microsoft-edge" or "msedge" => "msedge",
        "firefox" or "firefox-bin" or "firefox-esr" => "firefox",
        "zoom" or "ZoomWebviewHost" => "Zoom",
        "teams" or "teams-for-linux" => "Teams",
        "discord" or "Discord" => "Discord",
        "slack" => "slack",
        "telegram-desktop" or "Telegram" => "Telegram",
        "signal-desktop" => "Signal",
        "skypeforlinux" => "Skype",
        "whatsapp-for-linux" => "WhatsApp",
        var other => other,
    };

    /// <summary>"firefox" → "Firefox", "obs" → "OBS Studio"; an app's own name as it gives it otherwise.</summary>
    internal static string NameOf(string name) => Key(name) switch
    {
        "chrome" => "Chrome", "chromium" => "Chromium", "brave" => "Brave", "msedge" => "Edge", "firefox" => "Firefox",
        "slack" => "Slack", "obs" => "OBS Studio",
        var key when key.Length > 0 && char.IsLower(key[0]) && !key.Contains(' ') => char.ToUpperInvariant(key[0]) + key[1..],
        var key => key,
    };

    static void Unref(IntPtr operation)
    {
        if (operation != IntPtr.Zero) pa_operation_unref(operation);
    }

    [StructLayout(LayoutKind.Sequential)] struct PollFd { public int Fd; public short Events, Revents; }

    [DllImport("libc", SetLastError = true)] static extern int inotify_init1(int flags);
    [DllImport("libc", SetLastError = true)] static extern int inotify_add_watch(int fd, string path, uint mask);
    [DllImport("libc", EntryPoint = "read", SetLastError = true)] static extern nint Read(int fd, byte[] buffer, nint count);
    [DllImport("libc", EntryPoint = "poll", SetLastError = true)] static extern int Poll(ref PollFd fds, nuint count, int timeout);
    [DllImport("libc")] static extern int close(int fd);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ContextCallback(IntPtr context, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SubscribeCallback(IntPtr context, int type, uint index, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InfoCallback(IntPtr context, IntPtr info, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ListCallback(IntPtr context, IntPtr info, int end, IntPtr userdata);

    const string Pulse = "libpulse.so.0";

    [DllImport(Pulse)] static extern IntPtr pa_threaded_mainloop_new();
    [DllImport(Pulse)] static extern int pa_threaded_mainloop_start(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_stop(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_free(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_lock(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_unlock(IntPtr loop);
    [DllImport(Pulse)] static extern IntPtr pa_threaded_mainloop_get_api(IntPtr loop);
    [DllImport(Pulse)] static extern IntPtr pa_context_new(IntPtr api, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Pulse)] static extern void pa_context_unref(IntPtr context);
    [DllImport(Pulse)] static extern int pa_context_connect(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string? server, int flags, IntPtr spawnApi);
    [DllImport(Pulse)] static extern void pa_context_disconnect(IntPtr context);
    [DllImport(Pulse)] static extern int pa_context_get_state(IntPtr context);
    [DllImport(Pulse)] static extern void pa_context_set_state_callback(IntPtr context, ContextCallback? callback, IntPtr userdata);
    [DllImport(Pulse)] static extern void pa_context_set_subscribe_callback(IntPtr context, SubscribeCallback? callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_subscribe(IntPtr context, uint mask, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_get_server_info(IntPtr context, InfoCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_get_source_info_list(IntPtr context, ListCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_get_source_output_info_list(IntPtr context, ListCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_source_mute_by_name(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int mute, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_proplist_gets(IntPtr proplist, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport(Pulse)] static extern void pa_operation_unref(IntPtr operation);
}
