using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace WinNotch;

/// <summary>
/// Which apps use the microphone or the camera, from the record Windows keeps for its own privacy settings
/// ("recently used"). Windows reports record changes; active entries are also rechecked for crashed apps.
/// </summary>
internal sealed class PrivacyMonitor : IDisposable
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

    readonly Dispatcher _ui;
    readonly DispatcherTimer _settle;
    readonly DispatcherTimer _verify;
    readonly DispatcherTimer _microphoneSoon;
    MicrophoneActivity? _microphoneActivity;
    CameraActivity? _cameraActivity;
    IReadOnlyList<SensorUse>? _liveMicrophone, _liveCamera;
    readonly HashSet<string> _observedLiveCameras = new(StringComparer.OrdinalIgnoreCase);
    bool _readingMicrophone, _readMicrophoneAgain;
    readonly List<Watch> _watches = new();
    readonly Dictionary<string, string> _exeNames = new(StringComparer.OrdinalIgnoreCase);
    volatile bool _disposed;

    /// <summary>The apps using a sensor now, camera first, then by when they started.</summary>
    public IReadOnlyList<SensorUse> InUse { get; private set; } = Array.Empty<SensorUse>();

    /// <summary>An app started or stopped using a sensor. Raised on the UI thread.</summary>
    public event Action? Changed;

    public PrivacyMonitor(Dispatcher ui) : this(ui, Store) { }

    /// <param name="store">Where the record is: Windows' own, or a test copy.</param>
    internal PrivacyMonitor(Dispatcher ui, string store)
    {
        _ui = ui;
        // An app starting to record writes several values in a row; look once they're written.
        _settle = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => { _settle!.Stop(); Check(); }, ui);
        _settle.Stop();
        _verify = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Check(), ui);
        _verify.Stop();
        _microphoneSoon = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background,
            (_, _) => { _microphoneSoon!.Stop(); _ = RefreshMicrophoneAsync(); }, ui);
        _microphoneSoon.Stop();
        foreach (var (sensor, name) in new[] { (Sensor.Microphone, "microphone"), (Sensor.Camera, "webcam") })
        {
            var key = Registry.CurrentUser.OpenSubKey($@"{store}\{name}");
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
        InUse = Scan();
        if (InUse.Count > 0) _verify.Start();
        if (store == Store) { _ = StartMicrophoneAsync(); _ = StartCameraAsync(); }
    }

    async Task StartMicrophoneAsync()
    {
        try
        {
            var source = await Task.Run(() => new MicrophoneActivity());
            if (_disposed) { _ = Task.Run(source.Dispose); return; }
            _microphoneActivity = source;
            source.Changed += () => _ui.InvokeAsync(() =>
            {
                if (_disposed) return;
                _microphoneSoon.Stop();
                _microphoneSoon.Start();
            });
            await RefreshMicrophoneAsync();
        }
        catch (Exception ex) { Diag.Log("Live microphone monitor: " + ex.GetType().Name); }
    }

    async Task RefreshMicrophoneAsync()
    {
        if (_disposed || _microphoneActivity is not { } source) return;
        if (_readingMicrophone) { _readMicrophoneAgain = true; return; }
        _readingMicrophone = true;
        try
        {
            do
            {
                _readMicrophoneAgain = false;
                var current = await Task.Run(source.Read);
                if (!_disposed) SetLive(Sensor.Microphone, current);
            } while (_readMicrophoneAgain && !_disposed);
        }
        catch (Exception ex)
        {
            Diag.Log("Live microphone activity: " + ex.GetType().Name);
            if (!_disposed) { _liveMicrophone = null; Check(); }
        }
        finally { _readingMicrophone = false; }
    }

    async Task StartCameraAsync()
    {
        CameraActivity? source = null;
        try
        {
            source = await Task.Run(() => new CameraActivity());
            if (_disposed) { _ = Task.Run(source.Dispose); return; }
            _cameraActivity = source;
            source.Changed += current => _ui.InvokeAsync(() => { if (!_disposed) SetLive(Sensor.Camera, current); });
            await Task.Run(source.Start);
        }
        catch (Exception ex)
        {
            Diag.Log("Live camera monitor: " + ex.GetType().Name);
            if (source != null) await Task.Run(source.Dispose);
            _cameraActivity = null;
        }
    }

    void SetLive(Sensor sensor, IReadOnlyList<SensorUse> current)
    {
        var previous = sensor == Sensor.Microphone ? _liveMicrophone : _liveCamera;
        var stable = SensorRecord.StableLive(current, previous ?? Array.Empty<SensorUse>());
        if (sensor == Sensor.Microphone) _liveMicrophone = stable;
        else
        {
            _liveCamera = stable;
            foreach (var use in stable) _observedLiveCameras.Add(use.Key);
        }
        Check();
    }

    static bool Arm(Watch watch)
        => RegNotifyChangeKeyValue(watch.Key.Handle, true, NotifyName | NotifyLastSet | NotifyThreadAgnostic, watch.Signal.SafeWaitHandle, true) == 0;

    void OnSignal(Watch watch)
    {
        if (_disposed) return;
        try { Arm(watch); } catch (ObjectDisposedException) { return; } // a notification only fires once: ask for the next one
        _ui.InvokeAsync(() => { if (_disposed) return; _settle.Stop(); _settle.Start(); });
    }

    void Check()
    {
        var now = Scan();
        if (now.Count > 0) _verify.Start(); else _verify.Stop();
        if (now.SequenceEqual(InUse)) return;
        InUse = now;
        Diag.Log("Sensors in use: " + (now.Count == 0 ? "none" : string.Join(", ", now.Select(u => $"{u.Name} ({u.Sensor})"))));
        Changed?.Invoke();
    }

    List<SensorUse> Scan()
    {
        var uses = new List<SensorUse>();
        foreach (var watch in _watches)
        {
            try { uses.AddRange(Active(watch.Sensor, SensorRecord.Read(watch.Key), SensorRecord.IsRunning, DateTime.UtcNow, ExeName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
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

    internal static IReadOnlyList<SensorUse> StableLive(IEnumerable<SensorUse> current, IReadOnlyList<SensorUse> previous)
        => SensorRecord.StableLive(current, previous);

    internal static IEnumerable<SensorUse> Active(Sensor sensor, IEnumerable<SensorEntry> entries, Func<string, DateTime, bool> isRunning,
                                                  DateTime nowUtc, Func<string, string>? exeName = null)
        => SensorRecord.Active(sensor, entries, isRunning, nowUtc, exeName);

    internal static bool MatchesRecording(string recordedPath, DateTime since, string processPath, DateTime processStartedUtc)
        => SensorRecord.MatchesRecording(recordedPath, since, processPath, processStartedUtc);

    internal static string NameOfProcess(string process) => SensorRecord.NameOfProcess(process);

    internal static string NameOfPackage(string package) => SensorRecord.NameOfPackage(package);

    string ExeName(string path) => SensorRecord.ExeName(path, _exeNames);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settle.Stop();
        _verify.Stop();
        _microphoneSoon.Stop();
        if (_microphoneActivity is { } microphone) _ = Task.Run(microphone.Dispose);
        if (_cameraActivity is { } camera) _ = Task.Run(camera.Dispose);
        foreach (var watch in _watches)
        {
            watch.Wait?.Unregister(null);
            watch.Key.Dispose(); // closing the key ends its notification
            watch.Signal.Dispose();
        }
        _watches.Clear();
    }
}
