using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Linux;

/// <summary>
/// Volume and levels through the PulseAudio client library, which is also how PipeWire is spoken to on current
/// desktops (pipewire-pulse). The server announces every change; nothing is polled. The equalizer's levels come
/// from a small recording of the default output's monitor, open only between <see cref="StartMeter"/> and
/// <see cref="StopMeter"/>: 8 kHz mono, of which only the loudness of each 10 ms is kept.
///
/// All calls into the library happen under its main loop's lock, or on its thread in a callback.
/// </summary>
public sealed class LinuxAudioPlatform : IAudioPlatform
{
    const string DefaultSink = "@DEFAULT_SINK@", DefaultMonitor = "@DEFAULT_MONITOR@";
    const int ContextReady = 4, ContextFailed = 5, ContextTerminated = 6;
    const int ContextNoFail = 2; // wait for the sound server instead of failing when it isn't up yet
    const uint SubscribeSink = 0x0001, SubscribeServer = 0x0080;
    const int FacilityMask = 0x000F, FacilitySink = 0, FacilityServer = 7;
    const uint VolumeNorm = 0x10000;
    const int SampleFloat32 = 5, MeterRate = 8000, ChunkSamples = MeterRate / 100;
    const uint StreamAdjustLatency = 0x2000;
    // Where the fields this needs sit in pa_sink_info and pa_server_info (64-bit; the layout is part of the
    // library's ABI and has not changed since PulseAudio 1.0).
    const int SinkVolumeChannels = 172, SinkVolumeValues = 176, SinkMute = 304, ServerDefaultSinkName = 48;
    const int MaxFailures = 6;

    // The library keeps these pointers: they must live as long as this does.
    readonly ContextCallback _onState;
    readonly SubscribeCallback _onEvent;
    readonly InfoCallback _onServer;
    readonly SinkCallback _onSink;
    readonly StreamCallback _onRead;

    readonly object _gate = new();
    readonly LevelEnvelope _levels = new();
    IntPtr _loop, _context, _stream;
    bool _started, _unavailable, _known, _muted, _meterWanted;
    volatile bool _meterWorking;
    uint _raw;
    byte _channels = 2;
    string? _defaultSinkName;
    int _failures;
    double _chunkSum;
    int _chunkCount;

    public event Action<float, bool>? VolumeChanged;
    public event Action? DefaultDeviceChanged;

    public LinuxAudioPlatform()
    {
        _onState = OnState;
        _onEvent = OnEvent;
        _onServer = OnServer;
        _onSink = OnSink;
        _onRead = OnRead;
    }

    public void Start()
    {
        if (_unavailable) return;
        try
        {
            if (_loop != IntPtr.Zero)
            {
                // Called again after the default output changed: read the new one, quietly.
                Locked(() => { if (Ready) { RequestSink(announce: false); if (_stream != IntPtr.Zero) { CloseStream(); OpenStream(); } } });
                return;
            }
            _started = true;
            _loop = pa_threaded_mainloop_new();
            if (_loop == IntPtr.Zero || pa_threaded_mainloop_start(_loop) < 0) { _unavailable = true; return; }
            Locked(Connect);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true; // no PulseAudio client library: no volume, and the bars bounce by themselves
        }
    }

    public void Stop()
    {
        if (_loop == IntPtr.Zero) return;
        _started = false;
        Locked(() =>
        {
            CloseStream();
            if (_context != IntPtr.Zero)
            {
                pa_context_set_state_callback(_context, null, IntPtr.Zero);
                pa_context_disconnect(_context);
                pa_context_unref(_context);
                _context = IntPtr.Zero;
            }
        });
        pa_threaded_mainloop_stop(_loop);
        pa_threaded_mainloop_free(_loop);
        _loop = IntPtr.Zero;
        lock (_gate) _known = false;
    }

    public bool TryGetVolume(out float level, out bool muted)
    {
        lock (_gate)
        {
            level = LevelOf(_raw);
            muted = _muted;
            return _known;
        }
    }

    public void SetVolume(float level)
    {
        uint raw = (uint)Math.Round(Math.Clamp(level, 0f, 1f) * VolumeNorm);
        byte channels;
        // Remembered at once: the server's own report of this change then looks like no change, and isn't
        // announced as one made by somebody else.
        lock (_gate) { if (!_known) return; _raw = raw; channels = _channels; }
        Locked(() =>
        {
            if (!Ready) return;
            var volume = new CVolume { Channels = channels };
            unsafe { for (int i = 0; i < channels; i++) volume.Values[i] = raw; }
            Unref(pa_context_set_sink_volume_by_name(_context, DefaultSink, ref volume, IntPtr.Zero, IntPtr.Zero));
        });
    }

    public void SetMute(bool mute)
    {
        lock (_gate) { if (!_known) return; _muted = mute; }
        Locked(() => { if (Ready) Unref(pa_context_set_sink_mute_by_name(_context, DefaultSink, mute ? 1 : 0, IntPtr.Zero, IntPtr.Zero)); });
    }

    public void ToggleMute()
    {
        bool muted;
        lock (_gate) muted = _muted;
        SetMute(!muted);
    }

    public void StartMeter()
    {
        if (_meterWanted) return;
        _meterWanted = true;
        _levels.Clear();
        if (_loop != IntPtr.Zero) Locked(() => { if (Ready) OpenStream(); });
    }

    public void StopMeter()
    {
        if (!_meterWanted) return;
        _meterWanted = false;
        if (_loop != IntPtr.Zero) Locked(CloseStream);
        _meterWorking = false;
    }

    public bool MeterWorking => _meterWorking;

    // ───────────── outputs ─────────────

    // Where the fields sit in pa_sink_info (64-bit, checked against introspect.h).
    const int SinkName = 0, SinkDescription = 16, SinkProplist = 344, SinkActivePort = 392, PortName = 0;
    List<AudioOutput>? _readingOutputs;
    bool _outputsDone;
    ListCallback? _onOutput;

    /// <summary>The server's sinks: each with its description, and an icon from its form factor or port.</summary>
    public IReadOnlyList<AudioOutput> Outputs(out string? current)
    {
        current = _defaultSinkName;
        if (_loop == IntPtr.Zero) return Array.Empty<AudioOutput>();
        _onOutput ??= OnOutput;
        List<AudioOutput> found;
        pa_threaded_mainloop_lock(_loop);
        try
        {
            if (!Ready) return Array.Empty<AudioOutput>();
            _readingOutputs = new();
            _outputsDone = false;
            IntPtr operation = pa_context_get_sink_info_list(_context, _onOutput, IntPtr.Zero);
            if (operation == IntPtr.Zero) return Array.Empty<AudioOutput>();
            while (!_outputsDone && Ready) pa_threaded_mainloop_wait(_loop);
            pa_operation_unref(operation);
            found = _readingOutputs;
            _readingOutputs = null;
        }
        finally { pa_threaded_mainloop_unlock(_loop); }
        return found;
    }

    void OnOutput(IntPtr context, IntPtr info, int end, IntPtr userdata)
    {
        try
        {
            if (end != 0 || info == IntPtr.Zero) { _outputsDone = true; pa_threaded_mainloop_signal(_loop, 0); return; }
            string name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, SinkName)) ?? "";
            string description = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, SinkDescription)) ?? name;
            IntPtr props = Marshal.ReadIntPtr(info, SinkProplist);
            string form = props == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pa_proplist_gets(props, "device.form_factor")) ?? "";
            IntPtr port = Marshal.ReadIntPtr(info, SinkActivePort);
            string portName = port == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(port, PortName)) ?? "";
            if (name.Length > 0) _readingOutputs?.Add(new AudioOutput(name, description, GlyphOf(form, portName)));
        }
        catch { }
    }

    internal static string GlyphOf(string formFactor, string port)
    {
        if (formFactor is "headphone") return AudioOutput.Headphones;
        if (formFactor is "headset" or "handset" or "hands-free") return AudioOutput.Headset;
        if (formFactor is "tv" or "display" || port.Contains("hdmi", StringComparison.OrdinalIgnoreCase) || port.Contains("displayport", StringComparison.OrdinalIgnoreCase))
            return AudioOutput.Display;
        if (port.Contains("headphone", StringComparison.OrdinalIgnoreCase)) return AudioOutput.Headphones;
        return AudioOutput.Speakers;
    }

    public bool SetDefaultOutput(string id)
    {
        bool sent = false;
        Locked(() => { if (Ready) { IntPtr op = pa_context_set_default_sink(_context, id, IntPtr.Zero, IntPtr.Zero); sent = op != IntPtr.Zero; Unref(op); } });
        return sent;
    }
    public float MeterBar(int bar, int bars) => _levels.Bar(bar, bars);

    static float LevelOf(uint raw) => Math.Clamp((float)raw / VolumeNorm, 0f, 1f);

    bool Ready => _context != IntPtr.Zero && pa_context_get_state(_context) == ContextReady;

    /// <summary>Runs <paramref name="action"/> with the library's lock held; on its own thread the lock is already ours.</summary>
    void Locked(Action action)
    {
        if (_loop == IntPtr.Zero) return;
        if (pa_threaded_mainloop_in_thread(_loop) != 0) { action(); return; }
        pa_threaded_mainloop_lock(_loop);
        try { action(); }
        finally { pa_threaded_mainloop_unlock(_loop); }
    }

    void Connect()
    {
        _context = pa_context_new(pa_threaded_mainloop_get_api(_loop), "Holm");
        if (_context == IntPtr.Zero) return;
        pa_context_set_state_callback(_context, _onState, IntPtr.Zero);
        if (pa_context_connect(_context, null, ContextNoFail, IntPtr.Zero) < 0) Reconnect(_context);
    }

    void OnState(IntPtr context, IntPtr userdata)
    {
        try
        {
            if (context != _context) return;
            int state = pa_context_get_state(context);
            if (state == ContextReady)
            {
                _failures = 0;
                pa_context_set_subscribe_callback(context, _onEvent, IntPtr.Zero);
                Unref(pa_context_subscribe(context, SubscribeSink | SubscribeServer, IntPtr.Zero, IntPtr.Zero));
                Unref(pa_context_get_server_info(context, _onServer, IntPtr.Zero));
                RequestSink(announce: false);
                if (_meterWanted) OpenStream();
            }
            else if (state is ContextFailed or ContextTerminated)
            {
                lock (_gate) _known = false;
                _meterWorking = false;
                _stream = IntPtr.Zero; // it went with the connection
                if (_started) Reconnect(context);
            }
        }
        catch { }
    }

    /// <summary>
    /// The sound server went away (it restarts on a logout, an update, a crash): connect afresh, which waits for it
    /// to come back. A connection that keeps failing outright is given up after a few tries.
    /// </summary>
    void Reconnect(IntPtr failed)
    {
        if (++_failures > MaxFailures) return;
        int wait = 2000 * _failures;
        _ = Task.Run(async () =>
        {
            await Task.Delay(wait).ConfigureAwait(false);
            if (!_started || _loop == IntPtr.Zero) return;
            Locked(() =>
            {
                if (_context != failed) return;
                pa_context_set_state_callback(failed, null, IntPtr.Zero);
                pa_context_unref(failed);
                _context = IntPtr.Zero;
                Connect();
            });
        });
    }

    void OnEvent(IntPtr context, int type, uint index, IntPtr userdata)
    {
        try
        {
            int facility = type & FacilityMask;
            if (facility == FacilitySink) RequestSink(announce: true);
            else if (facility == FacilityServer) Unref(pa_context_get_server_info(context, _onServer, IntPtr.Zero));
        }
        catch { }
    }

    void RequestSink(bool announce)
        => Unref(pa_context_get_sink_info_by_name(_context, DefaultSink, _onSink, new IntPtr(announce ? 1 : 0)));

    void OnServer(IntPtr context, IntPtr info, IntPtr userdata)
    {
        try
        {
            if (info == IntPtr.Zero) return;
            string name = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(info, ServerDefaultSinkName)) ?? "";
            string? before = _defaultSinkName;
            _defaultSinkName = name;
            if (before == null || before == name) return;
            // Another output: its volume is not a change of volume.
            RequestSink(announce: false);
            if (_stream != IntPtr.Zero) { CloseStream(); OpenStream(); }
            DefaultDeviceChanged?.Invoke();
        }
        catch { }
    }

    void OnSink(IntPtr context, IntPtr info, int end, IntPtr userdata)
    {
        try
        {
            if (end != 0 || info == IntPtr.Zero) return;
            int channels = Math.Clamp(Marshal.ReadByte(info, SinkVolumeChannels), (byte)1, (byte)32);
            ulong sum = 0;
            for (int i = 0; i < channels; i++) sum += (uint)Marshal.ReadInt32(info, SinkVolumeValues + 4 * i);
            uint raw = (uint)(sum / (ulong)channels);
            bool muted = Marshal.ReadInt32(info, SinkMute) != 0;

            bool changed, known;
            lock (_gate)
            {
                known = _known;
                changed = raw != _raw || muted != _muted;
                (_raw, _muted, _channels, _known) = (raw, muted, (byte)channels, true);
            }
            if (known && changed && userdata != IntPtr.Zero) VolumeChanged?.Invoke(LevelOf(raw), muted);
        }
        catch { }
    }

    void OpenStream()
    {
        if (_stream != IntPtr.Zero) return;
        var spec = new SampleSpec { Format = SampleFloat32, Rate = MeterRate, Channels = 1 };
        IntPtr stream = pa_stream_new(_context, "Loudness for the equalizer", ref spec, IntPtr.Zero);
        if (stream == IntPtr.Zero) return;
        pa_stream_set_read_callback(stream, _onRead, IntPtr.Zero);
        // Hand the sound over in pieces of 10 ms.
        var attr = new BufferAttr { MaxLength = uint.MaxValue, TargetLength = uint.MaxValue, PreBuffer = uint.MaxValue, MinRequest = uint.MaxValue, FragmentSize = ChunkSamples * sizeof(float) };
        if (pa_stream_connect_record(stream, DefaultMonitor, ref attr, StreamAdjustLatency) < 0)
        {
            pa_stream_unref(stream);
            return;
        }
        _chunkSum = 0;
        _chunkCount = 0;
        _stream = stream;
    }

    void CloseStream()
    {
        if (_stream == IntPtr.Zero) return;
        pa_stream_set_read_callback(_stream, null, IntPtr.Zero);
        pa_stream_disconnect(_stream);
        pa_stream_unref(_stream);
        _stream = IntPtr.Zero;
        _meterWorking = false;
    }

    unsafe void OnRead(IntPtr stream, UIntPtr available, IntPtr userdata)
    {
        try
        {
            while (pa_stream_peek(stream, out IntPtr data, out UIntPtr bytes) >= 0 && bytes != UIntPtr.Zero)
            {
                if (data != IntPtr.Zero) // null is a gap in the sound, with a length but nothing in it
                {
                    var samples = new ReadOnlySpan<float>((void*)data, (int)((ulong)bytes / sizeof(float)));
                    foreach (float sample in samples)
                    {
                        _chunkSum += sample * sample;
                        if (++_chunkCount < ChunkSamples) continue;
                        _levels.Push((float)Math.Sqrt(_chunkSum / ChunkSamples) * 4f); // the same gain as on Windows
                        _chunkSum = 0;
                        _chunkCount = 0;
                    }
                    _meterWorking = true;
                }
                pa_stream_drop(stream);
            }
        }
        catch { }
    }

    static void Unref(IntPtr operation)
    {
        if (operation != IntPtr.Zero) pa_operation_unref(operation);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SampleSpec { public int Format; public uint Rate; public byte Channels; }

    [StructLayout(LayoutKind.Sequential)]
    struct BufferAttr { public uint MaxLength, TargetLength, PreBuffer, MinRequest, FragmentSize; }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct CVolume { public byte Channels; public fixed uint Values[32]; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ContextCallback(IntPtr context, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SubscribeCallback(IntPtr context, int type, uint index, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void InfoCallback(IntPtr context, IntPtr info, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SinkCallback(IntPtr context, IntPtr info, int end, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StreamCallback(IntPtr stream, UIntPtr bytes, IntPtr userdata);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void ListCallback(IntPtr context, IntPtr info, int end, IntPtr userdata);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_wait(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_signal(IntPtr loop, int waitForAccept);
    [DllImport(Pulse)] static extern IntPtr pa_context_get_sink_info_list(IntPtr context, ListCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_default_sink(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_proplist_gets(IntPtr proplist, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    const string Pulse = "libpulse.so.0";

    [DllImport(Pulse)] static extern IntPtr pa_threaded_mainloop_new();
    [DllImport(Pulse)] static extern int pa_threaded_mainloop_start(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_stop(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_free(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_lock(IntPtr loop);
    [DllImport(Pulse)] static extern void pa_threaded_mainloop_unlock(IntPtr loop);
    [DllImport(Pulse)] static extern int pa_threaded_mainloop_in_thread(IntPtr loop);
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
    [DllImport(Pulse)] static extern IntPtr pa_context_get_sink_info_by_name(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, SinkCallback callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_sink_volume_by_name(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref CVolume volume, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern IntPtr pa_context_set_sink_mute_by_name(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int mute, IntPtr callback, IntPtr userdata);
    [DllImport(Pulse)] static extern void pa_operation_unref(IntPtr operation);

    [DllImport(Pulse)] static extern IntPtr pa_stream_new(IntPtr context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, ref SampleSpec spec, IntPtr map);
    [DllImport(Pulse)] static extern void pa_stream_unref(IntPtr stream);
    [DllImport(Pulse)] static extern int pa_stream_connect_record(IntPtr stream, [MarshalAs(UnmanagedType.LPUTF8Str)] string? device, ref BufferAttr attr, uint flags);
    [DllImport(Pulse)] static extern int pa_stream_disconnect(IntPtr stream);
    [DllImport(Pulse)] static extern void pa_stream_set_read_callback(IntPtr stream, StreamCallback? callback, IntPtr userdata);
    [DllImport(Pulse)] static extern int pa_stream_peek(IntPtr stream, out IntPtr data, out UIntPtr bytes);
    [DllImport(Pulse)] static extern int pa_stream_drop(IntPtr stream);
}
