using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// Who records, as the menu bar's orange and green dots know it. The microphone: Core Audio's list of its client
/// processes says which ones take input (macOS 14.2 and later; before that, only that the default microphone is in
/// use). The camera: CoreMediaIO says a camera is running, not for whom, so the dot shows without a name. No
/// permission is needed for either, and both report changes themselves.
/// </summary>
public sealed unsafe class MacSensorPlatform : ISensorPlatform
{
    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string CoreMediaIO = "/System/Library/Frameworks/CoreMediaIO.framework/CoreMediaIO";
    const uint SystemObject = 1;
    static readonly uint Global = Code("glob"), Input = Code("inpt"), ProcessList = Code("prs#"), ProcessPid = Code("ppid"),
        RunningInput = Code("piri"), DefaultInput = Code("dIn "), RunningSomewhere = Code("gone"), Mute = Code("mute"),
        Devices = Code("dev#");

    static MacSensorPlatform? _current;
    readonly object _gate = new();
    readonly HashSet<uint> _watchedProcesses = new();
    readonly Dictionary<(bool, string), DateTime> _since = new();
    IReadOnlyList<SensorActivity> _inUse = Array.Empty<SensorActivity>();
    bool? _muted;
    uint _input;
    bool _started, _processesSupported = true;
    readonly HashSet<uint> _watchedCameras = new();

    public event Action? Changed;

    public IReadOnlyList<SensorActivity> InUse { get { lock (_gate) return _inUse; } }

    static uint Code(string text) => (uint)(text[0] << 24 | text[1] << 16 | text[2] << 8 | text[3]);

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _current = this;
            try
            {
                var processes = new PropertyAddress(ProcessList, Global, 0);
                _processesSupported = AudioObjectHasProperty(SystemObject, ref processes) != 0;
                if (_processesSupported) AudioObjectAddPropertyListener(SystemObject, ref processes, &OnAudioChanged, IntPtr.Zero);
                var input = new PropertyAddress(DefaultInput, Global, 0);
                AudioObjectAddPropertyListener(SystemObject, ref input, &OnAudioChanged, IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
            try
            {
                var devices = new PropertyAddress(Devices, Global, 0);
                CMIOObjectAddPropertyListener(SystemObject, ref devices, &OnCameraChanged, IntPtr.Zero);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        }
        Refresh();
    }

    public void Stop()
    {
        lock (_gate)
        {
            _started = false;
            _current = null;
        }
    }

    [UnmanagedCallersOnly]
    static int OnAudioChanged(uint objectId, uint count, PropertyAddress* addresses, IntPtr clientData)
    {
        try { _current?.Refresh(); } catch { }
        return 0;
    }

    [UnmanagedCallersOnly]
    static int OnCameraChanged(uint objectId, uint count, PropertyAddress* addresses, IntPtr clientData)
    {
        try { _current?.Refresh(); } catch { }
        return 0;
    }

    void Refresh()
    {
        bool changed;
        lock (_gate)
        {
            if (!_started) return;
            var found = new List<(bool Camera, string Name, string Key)>();
            if (Cameras()) found.Add((true, "An app", "camera"));
            found.AddRange(Microphones().Select(m => (false, m.Name, m.Key)));
            WatchInput();
            bool? muted = ReadMute(_input);

            var now = DateTime.UtcNow;
            foreach (var key in _since.Keys.Where(k => !found.Any(f => (f.Camera, f.Key) == k)).ToList()) _since.Remove(key);
            var list = found.DistinctBy(f => (f.Camera, f.Key)).Select(f =>
            {
                if (!_since.TryGetValue((f.Camera, f.Key), out var since)) _since[(f.Camera, f.Key)] = since = now;
                return new SensorActivity(f.Camera, f.Name, f.Key, since);
            }).OrderBy(u => u.Camera ? 0 : 1).ThenBy(u => u.Since).ToList();
            changed = !list.SequenceEqual(_inUse) || muted != _muted;
            _inUse = list;
            _muted = muted;
        }
        if (changed) try { Changed?.Invoke(); } catch { }
    }

    /// <summary>The processes taking input; each is watched so it is noticed when it starts or stops.</summary>
    List<(string Name, string Key)> Microphones()
    {
        var found = new List<(string, string)>();
        if (!_processesSupported)
        {
            // Before macOS 14.2: only that the default microphone runs.
            if (_input != 0 && ReadUInt(_input, RunningSomewhere, Global) is > 0) found.Add(("An app", "microphone"));
            return found;
        }
        foreach (uint process in ReadList(SystemObject, ProcessList, Global, audio: true))
        {
            if (_watchedProcesses.Add(process))
            {
                var running = new PropertyAddress(RunningInput, Global, 0);
                AudioObjectAddPropertyListener(process, ref running, &OnAudioChanged, IntPtr.Zero);
            }
            if (ReadUInt(process, RunningInput, Global) is not > 0) continue;
            if (ReadUInt(process, ProcessPid, Global) is not { } pid || pid == (uint)Environment.ProcessId) continue;
            var (name, key) = AppOf((int)pid);
            if (key.Length > 0 && !found.Any(f => f.Item2 == key)) found.Add((name, key));
        }
        return found;
    }

    bool Cameras()
    {
        foreach (uint device in ReadList(SystemObject, Devices, Global, audio: false))
        {
            var address = new PropertyAddress(RunningSomewhere, Global, 0);
            if (_watchedCameras.Add(device)) CMIOObjectAddPropertyListener(device, ref address, &OnCameraChanged, IntPtr.Zero);
            uint running = 0, used = 0;
            if (CMIOObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(uint), ref used, &running) == 0 && running != 0) return true;
        }
        return false;
    }

    void WatchInput()
    {
        uint input = ReadUInt(SystemObject, DefaultInput, Global) ?? 0;
        if (input == _input) return;
        _input = input;
        if (input == 0) return;
        var mute = new PropertyAddress(Mute, Input, 0);
        AudioObjectAddPropertyListener(input, ref mute, &OnAudioChanged, IntPtr.Zero);
        var running = new PropertyAddress(RunningSomewhere, Global, 0);
        AudioObjectAddPropertyListener(input, ref running, &OnAudioChanged, IntPtr.Zero);
    }

    static bool? ReadMute(uint device)
    {
        if (device == 0) return null;
        var address = new PropertyAddress(Mute, Input, 0);
        if (AudioObjectHasProperty(device, ref address) == 0) return null;
        uint value = 0, size = sizeof(uint);
        return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, &value) == 0 ? value != 0 : null;
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
        uint device;
        lock (_gate) device = _input;
        if (device == 0) return;
        var address = new PropertyAddress(Mute, Input, 0);
        uint value = mute ? 1u : 0u;
        AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(uint), &value);
    }

    static uint? ReadUInt(uint objectId, uint selector, uint scope)
    {
        var address = new PropertyAddress(selector, scope, 0);
        uint value = 0, size = sizeof(uint);
        return AudioObjectGetPropertyData(objectId, ref address, 0, IntPtr.Zero, ref size, &value) == 0 ? value : null;
    }

    static uint[] ReadList(uint objectId, uint selector, uint scope, bool audio)
    {
        var address = new PropertyAddress(selector, scope, 0);
        uint size = 0;
        int status = audio ? AudioObjectGetPropertyDataSize(objectId, ref address, 0, IntPtr.Zero, ref size)
                           : CMIOObjectGetPropertyDataSize(objectId, ref address, 0, IntPtr.Zero, ref size);
        if (status != 0 || size == 0) return Array.Empty<uint>();
        var list = new uint[size / sizeof(uint)];
        fixed (uint* data = list)
        {
            uint used = 0;
            status = audio ? AudioObjectGetPropertyData(objectId, ref address, 0, IntPtr.Zero, ref size, data)
                           : CMIOObjectGetPropertyData(objectId, ref address, 0, IntPtr.Zero, size, ref used, data);
        }
        return status == 0 ? list : Array.Empty<uint>();
    }

    /// <summary>
    /// The app a process belongs to: the outermost .app bundle in its path, so a browser's audio helper counts as
    /// the browser. Keys as the call list knows them ("chrome", "Zoom").
    /// </summary>
    static (string Name, string Key) AppOf(int pid)
    {
        var buffer = new byte[4096];
        int length = proc_pidpath(pid, buffer, (uint)buffer.Length);
        if (length <= 0) return ("", "");
        string path = Encoding.UTF8.GetString(buffer, 0, length);
        int app = path.IndexOf(".app/", StringComparison.Ordinal);
        string bundle = app > 0 ? Path.GetFileName(path[..app]) : Path.GetFileName(path);
        return bundle switch
        {
            "Google Chrome" or "Google Chrome Canary" => ("Chrome", "chrome"),
            "Microsoft Edge" => ("Edge", "msedge"),
            "Brave Browser" => ("Brave", "brave"),
            "Firefox" => ("Firefox", "firefox"),
            "Opera" => ("Opera", "opera"),
            "Vivaldi" => ("Vivaldi", "vivaldi"),
            "Chromium" => ("Chromium", "chrome"),
            "Arc" => ("Arc", "chrome"),
            "zoom.us" => ("Zoom", "Zoom"),
            "Microsoft Teams" or "Microsoft Teams (work or school)" or "Microsoft Teams classic" => ("Teams", "Teams"),
            "Slack" => ("Slack", "slack"),
            "Webex" or "Cisco Webex Meetings" => ("Webex", "Webex"),
            var other => (other, other),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    readonly struct PropertyAddress
    {
        public readonly uint Selector, Scope, Element;
        public PropertyAddress(uint selector, uint scope, uint element) => (Selector, Scope, Element) = (selector, scope, element);
    }

    [DllImport(CoreAudio)] static extern byte AudioObjectHasProperty(uint objectId, ref PropertyAddress address);
    [DllImport(CoreAudio)] static extern int AudioObjectGetPropertyDataSize(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size);
    [DllImport(CoreAudio)] static extern int AudioObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size, void* data);
    [DllImport(CoreAudio)] static extern int AudioObjectSetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint size, void* data);
    [DllImport(CoreAudio)] static extern int AudioObjectAddPropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);
    [DllImport(CoreMediaIO)] static extern int CMIOObjectGetPropertyDataSize(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size);
    [DllImport(CoreMediaIO)] static extern int CMIOObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint size, ref uint used, void* data);
    [DllImport(CoreMediaIO)] static extern int CMIOObjectAddPropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);
    [DllImport("/usr/lib/libproc.dylib")] static extern int proc_pidpath(int pid, byte[] buffer, uint size);
}
