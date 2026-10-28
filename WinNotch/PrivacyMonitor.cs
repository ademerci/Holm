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

internal enum Sensor { Microphone, Camera }

/// <summary>An app using the microphone or the camera right now.</summary>
/// <param name="Key">Its process name ("Discord") or, for a Store app, its package ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm").</param>
/// <param name="Since">When it started, UTC.</param>
internal sealed record SensorUse(Sensor Sensor, string Name, string Key, DateTime Since);

/// <summary>One app's entry in Windows' record of who used a sensor. Times are FILETIMEs; Stop is 0 while it is in use.</summary>
internal readonly record struct SensorEntry(string Name, bool NonPackaged, long Start, long Stop);

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

    /// <summary>A Store app's entry left behind by a crash would otherwise show forever; nobody records for half a day.</summary>
    static readonly TimeSpan StaleAfter = TimeSpan.FromHours(12);

    static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["brave"] = "Brave", ["opera"] = "Opera",
        ["librewolf"] = "LibreWolf", ["Discord"] = "Discord", ["Teams"] = "Teams", ["ms-teams"] = "Teams", ["Zoom"] = "Zoom",
        ["slack"] = "Slack", ["Telegram"] = "Telegram", ["Signal"] = "Signal", ["Skype"] = "Skype", ["WhatsApp"] = "WhatsApp",
        ["obs64"] = "OBS Studio", ["obs32"] = "OBS Studio", ["Resolve"] = "DaVinci Resolve", ["CiscoCollabHost"] = "Webex",
        ["webexmta"] = "Webex", ["WhatsAppDesktop"] = "WhatsApp", ["WindowsCamera"] = "Camera",
        ["WindowsSoundRecorder"] = "Sound Recorder", ["MSTeams"] = "Teams", ["MicrosoftTeams"] = "Teams",
    };

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
        var stable = StableLive(current, previous ?? Array.Empty<SensorUse>());
        if (sensor == Sensor.Microphone) _liveMicrophone = stable;
        else
        {
            _liveCamera = stable;
            foreach (var use in stable) _observedLiveCameras.Add(use.Key);
        }
        Check();
    }

    internal static IReadOnlyList<SensorUse> StableLive(IEnumerable<SensorUse> current, IReadOnlyList<SensorUse> previous)
        => current.Select(use => use with { Since = previous.FirstOrDefault(old => old.Sensor == use.Sensor
            && old.Key.Equals(use.Key, StringComparison.OrdinalIgnoreCase))?.Since ?? use.Since }).ToArray();

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
            try { uses.AddRange(Active(watch.Sensor, Read(watch.Key), IsRunning, DateTime.UtcNow, ExeName)); }
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

    static IEnumerable<SensorEntry> Read(RegistryKey sensor)
    {
        var entries = new List<SensorEntry>();
        foreach (string name in sensor.GetSubKeyNames())
        {
            using var key = sensor.OpenSubKey(name);
            if (key == null) continue;
            if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string exe in key.GetSubKeyNames())
                {
                    using var app = key.OpenSubKey(exe);
                    if (app != null) entries.Add(new SensorEntry(exe, true, Time(app, "LastUsedTimeStart"), Time(app, "LastUsedTimeStop")));
                }
            }
            else entries.Add(new SensorEntry(name, false, Time(key, "LastUsedTimeStart"), Time(key, "LastUsedTimeStop")));
        }
        return entries;
    }

    static long Time(RegistryKey key, string value) => key.GetValue(value) switch
    {
        long l => l,
        int i => i,
        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
        _ => 0,
    };

    /// <summary>
    /// The entries in use now: started and not stopped. An app that crashed while recording never writes its stop
    /// time, so the same desktop executable must still be running from before the recording started.
    /// A Store app must have started recording in the last 12 hours.
    /// </summary>
    internal static IEnumerable<SensorUse> Active(Sensor sensor, IEnumerable<SensorEntry> entries, Func<string, DateTime, bool> isRunning,
                                                  DateTime nowUtc, Func<string, string>? exeName = null)
    {
        foreach (var entry in entries)
        {
            if (entry.Start <= 0 || entry.Stop != 0) continue;
            DateTime since;
            try { since = DateTime.FromFileTimeUtc(entry.Start); }
            catch (ArgumentOutOfRangeException) { continue; }

            if (entry.NonPackaged)
            {
                string path = entry.Name.Replace('#', '\\');
                string process = Path.GetFileNameWithoutExtension(path);
                if (process.Length == 0 || !isRunning(path, since)) continue;
                yield return new SensorUse(sensor, exeName?.Invoke(path) ?? NameOfProcess(process), process, since);
            }
            else
            {
                if (nowUtc - since > StaleAfter) continue;
                yield return new SensorUse(sensor, NameOfPackage(entry.Name), entry.Name, since);
            }
        }
    }

    internal static bool MatchesRecording(string recordedPath, DateTime since, string processPath, DateTime processStartedUtc)
        => string.Equals(recordedPath, processPath, StringComparison.OrdinalIgnoreCase) && processStartedUtc <= since;

    static bool IsRunning(string path, DateTime since)
    {
        var found = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path));
        try
        {
            foreach (var process in found)
            {
                try
                {
                    if (MatchesRecording(path, since, Native.ProcessPath(process.Id), process.StartTime.ToUniversalTime())) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
            return false;
        }
        finally { foreach (var process in found) process.Dispose(); }
    }

    /// <summary>Known apps by their usual name, others by the description in the exe ("Google Chrome"), or its file name.</summary>
    string ExeName(string path)
    {
        string process = Path.GetFileNameWithoutExtension(path);
        if (KnownNames.TryGetValue(process, out var known)) return known;
        if (_exeNames.TryGetValue(path, out var cached)) return cached;
        string name = process;
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
            if (!string.IsNullOrEmpty(description) && description.Length <= 30) name = description;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ArgumentException) { }
        _exeNames[path] = name;
        return name;
    }

    internal static string NameOfProcess(string process) => KnownNames.TryGetValue(process, out var known) ? known : process;

    /// <summary>"5319275A.WhatsAppDesktop_cv1g1gvanyjgm" → "WhatsApp"; "Contoso.PhotoBooth_xyz" → "Photo Booth".</summary>
    internal static string NameOfPackage(string package)
    {
        string name = package.Split('_')[0];
        name = name[(name.LastIndexOf('.') + 1)..];
        if (KnownNames.TryGetValue(name, out var known)) return known;
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
        return name.Length > 0 ? name : package;
    }

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
