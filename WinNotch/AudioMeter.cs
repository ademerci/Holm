using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinNotch;

/// <summary>
/// How loud the sound is, as the equalizer shows it. The same logic as the Dynamic Island Windhawk mod: the level of
/// each short chunk (its RMS), a quick attack and a slow release, and the bars read the history a few chunks apart so
/// they move one after another. On top, a slow automatic gain so quiet music moves the bars as much as loud music.
/// Pure arithmetic, no audio API: the tests drive it with made-up levels.
/// </summary>
internal sealed class LevelEnvelope
{
    /// <summary>How many chunk levels are kept.</summary>
    public const int History = 64;
    /// <summary>Chunks between one bar and the next (each chunk is about 10 ms).</summary>
    public const int Step = 4;
    /// <summary>The mod's envelope: quick to rise for beats, slow to fall.</summary>
    public const float Attack = 0.7f, Release = 0.15f;

    readonly float[] _levels = new float[History];
    int _write;
    float _peak = MinPeak;
    const float MinPeak = 0.02f, PeakFall = 0.995f;

    /// <summary>Takes the level of one chunk (RMS of its samples, 0 to 1).</summary>
    public void Push(float rms)
    {
        rms = float.IsFinite(rms) ? Math.Clamp(rms, 0, 1) : 0;
        // The automatic gain: the loudest recent chunk counts as full height, and that falls back slowly.
        _peak = Math.Max(Math.Max(rms, _peak * PeakFall), MinPeak);
        float level = rms < 0.0005f ? 0 : MathF.Pow(Math.Clamp(rms / _peak, 0, 1), 0.8f);
        float last = _levels[(_write + History - 1) % History];
        float smoothed = level > last ? last + (level - last) * Attack : last + (level - last) * Release;
        lock (_levels)
        {
            _levels[_write % History] = smoothed;
            _write = (_write + 1) % History;
        }
    }

    /// <summary>The level bar <paramref name="bar"/> of <paramref name="bars"/> shows: the last bar the newest chunk.</summary>
    public float Bar(int bar, int bars)
    {
        int back = (bars - 1 - Math.Clamp(bar, 0, bars - 1)) * Step + 1;
        lock (_levels) return _levels[(_write + History * 2 - back) % History];
    }

    public void Clear()
    {
        lock (_levels) Array.Clear(_levels);
        _peak = MinPeak;
    }
}

/// <summary>
/// Reads how loud the sound coming out of the default speakers or headphones is (a WASAPI loopback stream), on a
/// thread of its own, only while the equalizer runs. Only the level is taken: nothing is recorded, kept or sent, and
/// it isn't the microphone, so Windows shows no recording sign.
/// </summary>
internal sealed class AudioMeter : IDisposable
{
    public LevelEnvelope Levels { get; } = new();

    Thread? _thread;
    CancellationTokenSource? _run;
    /// <summary>Whether the loopback stream is open, i.e. the bars show real levels.</summary>
    public bool Working { get; private set; }

    public void Start()
    {
        if (_thread != null) return;
        var run = _run = new CancellationTokenSource();
        _thread = new Thread(() => Run(run.Token)) { IsBackground = true, Name = "Holm audio meter", Priority = ThreadPriority.BelowNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _run?.Cancel();
        _run = null;
        _thread?.Join(500); // a thread still busy in Windows' audio stops by itself; its token says so
        _thread = null;
        Working = false;
        Levels.Clear();
    }

    public void Dispose() => Stop();

    void Run(CancellationToken stop)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                Capture(stop);
                failures = 0;
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException or ArgumentException)
            {
                Working = false;
                if (failures++ == 0) Diag.Log("Audio meter: " + ex.Message);
            }
            stop.WaitHandle.WaitOne(Math.Min(5000, 500 * Math.Max(1, failures)));
        }
    }

    const int Render = 0, Console = 0, ClsCtxAll = 0x17;
    const int ShareModeShared = 0, StreamFlagsLoopback = 0x00020000, BufferSilent = 0x2;
    static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    static readonly Guid IidCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00aa00389b71");

    /// <summary>Reads levels from the default output until it changes or we stop.</summary>
    void Capture(CancellationToken stop)
    {
        var enumerator = (VolumeService.IMMDeviceEnumerator)new VolumeService.MMDeviceEnumerator();
        IntPtr format = IntPtr.Zero;
        IAudioClient? client = null;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(Render, Console, out var device));
            Check(device.GetId(out string deviceId));
            var iid = IidAudioClient;
            Check(device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object o));
            client = (IAudioClient)o;
            Check(client.GetMixFormat(out format));
            var (channels, bits, isFloat, rate) = Describe(format);
            if (channels == 0 || !(isFloat && bits == 32 || !isFloat && bits == 16)) throw new InvalidOperationException($"mix format {bits} bit");
            Check(client.Initialize(ShareModeShared, StreamFlagsLoopback, 2_000_000, 0, format, IntPtr.Zero));
            var captureIid = IidCaptureClient;
            Check(client.GetService(ref captureIid, out object c));
            var capture = (IAudioCaptureClient)c;
            Check(client.Start());
            if (stop.IsCancellationRequested) return;
            Working = true;

            int chunk = Math.Max(64, rate / 100); // about 10 ms of frames per level
            double sum = 0;
            int frames = 0;
            var lastCheck = Environment.TickCount64;
            while (!stop.IsCancellationRequested)
            {
                if (stop.WaitHandle.WaitOne(10)) break;
                if (Environment.TickCount64 - lastCheck > 1000)
                {
                    lastCheck = Environment.TickCount64;
                    // Headphones plugged in or another output chosen: open the new one.
                    if (enumerator.GetDefaultAudioEndpoint(Render, Console, out var now) != 0 || now.GetId(out string id) != 0 || id != deviceId) break;
                }
                Check(capture.GetNextPacketSize(out uint packet));
                bool any = false;
                while (packet > 0 && !stop.IsCancellationRequested)
                {
                    Check(capture.GetBuffer(out IntPtr data, out uint count, out uint flags, out _, out _));
                    any = true;
                    bool silent = (flags & BufferSilent) != 0;
                    for (int i = 0; i < count; i++)
                    {
                        double frameSum = 0;
                        if (!silent)
                        {
                            for (int ch = 0; ch < channels; ch++)
                            {
                                int index = i * channels + ch;
                                double v = isFloat ? ReadFloat(data, index) : Marshal.ReadInt16(data, index * 2) / 32768.0;
                                frameSum += v * v;
                            }
                        }
                        sum += frameSum / channels;
                        if (++frames >= chunk)
                        {
                            Levels.Push((float)Math.Sqrt(sum / frames) * 4f); // the mod's gain before the envelope
                            sum = 0;
                            frames = 0;
                        }
                    }
                    Check(capture.ReleaseBuffer(count));
                    Check(capture.GetNextPacketSize(out packet));
                }
                // Nothing plays at all: Windows sends no packets, so the bars fall by themselves.
                if (!any) Levels.Push(0);
            }
            client.Stop();
        }
        finally
        {
            if (!stop.IsCancellationRequested) Working = false;
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            if (client != null) Marshal.ReleaseComObject(client);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    static double ReadFloat(IntPtr data, int index) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(data, index * 4));

    /// <summary>Channels, bits, whether the samples are floats, and the rate of a WAVEFORMATEX(TENSIBLE).</summary>
    static (int Channels, int Bits, bool Float, int Rate) Describe(IntPtr format)
    {
        int tag = (ushort)Marshal.ReadInt16(format, 0);
        int channels = (ushort)Marshal.ReadInt16(format, 2);
        int rate = Marshal.ReadInt32(format, 4);
        int bits = (ushort)Marshal.ReadInt16(format, 14);
        bool isFloat = tag == 3;
        if (tag == 0xFFFE && (ushort)Marshal.ReadInt16(format, 16) >= 22)
        {
            var sub = new byte[16];
            Marshal.Copy(format + 24, sub, 0, 16);
            isFloat = new Guid(sub) == FloatFormat;
        }
        return (channels, bits, isFloat, rate);
    }

    static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
