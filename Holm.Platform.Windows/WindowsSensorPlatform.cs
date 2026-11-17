using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>
/// The WPF app's PrivacyMonitor without its WPF timers: Windows' own record of who used the microphone and the
/// camera (watched for changes, rechecked every 5 s while something records, for apps that crashed), corrected by
/// the live sources of the WPF app (MicrophoneActivity, CameraActivity; linked). The microphone's mute is the WPF
/// app's VolumeService on the default capture device.
/// </summary>
public sealed class WindowsSensorPlatform : ISensorPlatform
{
    const string Store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    const uint NotifyName = 0x1, NotifyLastSet = 0x4, NotifyThreadAgnostic = 0x10000000;

    [DllImport("advapi32.dll")]
    static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, uint filter, SafeWaitHandle signal, bool asynchronous);

    sealed class Watch
    {
        public required Sensor Sensor;
        public required RegistryKey Key;
        public required AutoResetEvent Signal;
        public RegisteredWaitHandle? Wait;
    }

    readonly object _gate = new();
    readonly List<Watch> _watches = new();
    readonly Dictionary<string, string> _exeNames = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _observedLiveCameras = new(StringComparer.OrdinalIgnoreCase);
    readonly VolumeService _mic = VolumeService.Microphone();
    Timer? _settle, _verify, _microphoneSoon;
    MicrophoneActivity? _microphoneActivity;
    CameraActivity? _cameraActivity;
    IReadOnlyList<SensorUse>? _liveMicrophone, _liveCamera;
    IReadOnlyList<SensorUse> _uses = Array.Empty<SensorUse>();
    IReadOnlyList<SensorActivity> _inUse = Array.Empty<SensorActivity>();
    volatile bool _disposed = true;

    public event Action? Changed;

    public IReadOnlyList<SensorActivity> InUse { get { lock (_gate) return _inUse; } }

    public void Start()
    {
        if (!_disposed) return;
        _disposed = false;
        // An app starting to record writes several values in a row; look once they're written.
        _settle = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        _verify = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        _microphoneSoon = new Timer(_ => RefreshMicrophone(), null, Timeout.Infinite, Timeout.Infinite);
        foreach (var (sensor, name) in new[] { (Sensor.Microphone, "microphone"), (Sensor.Camera, "webcam") })
        {
            try
            {
                var key = Registry.CurrentUser.OpenSubKey($@"{Store}\{name}");
                if (key == null) continue;
                var watch = new Watch { Sensor = sensor, Key = key, Signal = new AutoResetEvent(false) };
                if (!Arm(watch))
                {
                    key.Dispose();
                    watch.Signal.Dispose();
                    continue;
                }
                watch.Wait = ThreadPool.RegisterWaitForSingleObject(watch.Signal, (state, _) => OnSignal((Watch)state!), watch, Timeout.Infinite, executeOnlyOnce: false);
                _watches.Add(watch);
            }
            catch (Exception ex) { Diag.Log("Sensor record: " + ex.GetType().Name); }
        }
        _mic.Changed += (_, _) => Changed?.Invoke();
        _mic.DefaultDeviceChanged += () => { _mic.Bind(); Changed?.Invoke(); };
        _mic.Bind();
        Check();
        _ = Task.Run(StartMicrophone);
        _ = Task.Run(StartCamera);
    }

    void StartMicrophone()
    {
        try
        {
            var source = new MicrophoneActivity();
            if (_disposed) { source.Dispose(); return; }
            _microphoneActivity = source;
            source.Changed += () => _microphoneSoon?.Change(150, Timeout.Infinite);
            RefreshMicrophone();
        }
        catch (Exception ex) { Diag.Log("Live microphone monitor: " + ex.GetType().Name); }
    }

    readonly object _micRead = new();

    void RefreshMicrophone()
    {
        if (_disposed || _microphoneActivity is not { } source) return;
        lock (_micRead)
        {
            try
            {
                var current = source.Read();
                if (!_disposed) SetLive(Sensor.Microphone, current);
            }
            catch (Exception ex)
            {
                Diag.Log("Live microphone activity: " + ex.GetType().Name);
                lock (_gate) _liveMicrophone = null;
                Check();
            }
        }
    }

    void StartCamera()
    {
        CameraActivity? source = null;
        try
        {
            source = new CameraActivity();
            if (_disposed) { source.Dispose(); return; }
            _cameraActivity = source;
            source.Changed += current => { if (!_disposed) SetLive(Sensor.Camera, current); };
            source.Start();
        }
        catch (Exception ex)
        {
            Diag.Log("Live camera monitor: " + ex.GetType().Name);
            source?.Dispose();
            _cameraActivity = null;
        }
    }

    void SetLive(Sensor sensor, IReadOnlyList<SensorUse> current)
    {
        lock (_gate)
        {
            var previous = sensor == Sensor.Microphone ? _liveMicrophone : _liveCamera;
            var stable = SensorRecord.StableLive(current, previous ?? Array.Empty<SensorUse>());
            if (sensor == Sensor.Microphone) _liveMicrophone = stable;
            else
            {
                _liveCamera = stable;
                foreach (var use in stable) _observedLiveCameras.Add(use.Key);
            }
        }
        Check();
    }

    static bool Arm(Watch watch)
        => RegNotifyChangeKeyValue(watch.Key.Handle, true, NotifyName | NotifyLastSet | NotifyThreadAgnostic, watch.Signal.SafeWaitHandle, true) == 0;

    void OnSignal(Watch watch)
    {
        if (_disposed) return;
        try { Arm(watch); } catch (ObjectDisposedException) { return; } // a notification only fires once: ask for the next one
        _settle?.Change(300, Timeout.Infinite);
    }

    void Check()
    {
        if (_disposed) return;
        bool changed;
        lock (_gate)
        {
            var now = Scan();
            _verify?.Change(now.Count > 0 ? 5000 : Timeout.Infinite, now.Count > 0 ? 5000 : Timeout.Infinite);
            changed = !now.SequenceEqual(_uses);
            if (changed)
            {
                _uses = now;
                _inUse = now.Select(u => new SensorActivity(u.Sensor == Sensor.Camera, u.Name, u.Key, u.Since)).ToList();
                Diag.Log("Sensors in use: " + (now.Count == 0 ? "none" : string.Join(", ", now.Select(u => $"{u.Name} ({u.Sensor})"))));
            }
        }
        if (changed) try { Changed?.Invoke(); } catch { }
    }

    List<SensorUse> Scan()
    {
        var uses = new List<SensorUse>();
        foreach (var watch in _watches)
        {
            try { uses.AddRange(SensorRecord.Active(watch.Sensor, SensorRecord.Read(watch.Key), SensorRecord.IsRunning, DateTime.UtcNow, path => SensorRecord.ExeName(path, _exeNames))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ObjectDisposedException)
            {
                Diag.Log("Sensor record unreadable: " + ex.Message);
            }
        }
        if (_liveMicrophone != null) { uses.RemoveAll(use => use.Sensor == Sensor.Microphone); uses.AddRange(_liveMicrophone); }
        if (_liveCamera != null)
        {
            // Legacy DirectShow virtual cameras are absent from Media Foundation reports; retain their registry fallback.
            uses.RemoveAll(use => use.Sensor == Sensor.Camera && _observedLiveCameras.Contains(use.Key));
            uses.AddRange(_liveCamera);
        }
        return uses.OrderBy(u => u.Sensor == Sensor.Camera ? 0 : 1).ThenBy(u => u.Since).ToList();
    }

    public bool TryGetMicrophoneMute(out bool muted) => _mic.TryGet(out _, out muted);

    public void SetMicrophoneMute(bool mute) => _mic.SetMute(mute);

    public void Stop()
    {
        if (_disposed) return;
        _disposed = true;
        _settle?.Dispose();
        _verify?.Dispose();
        _microphoneSoon?.Dispose();
        if (_microphoneActivity is { } microphone) _ = Task.Run(microphone.Dispose);
        if (_cameraActivity is { } camera) _ = Task.Run(camera.Dispose);
        foreach (var watch in _watches)
        {
            watch.Wait?.Unregister(null);
            watch.Key.Dispose(); // closing the key ends its notification
            watch.Signal.Dispose();
        }
        _watches.Clear();
        _mic.Shutdown();
    }
}
