using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static WinNotch.VolumeService;

namespace WinNotch;

/// <summary>Live recording sessions on every capture endpoint. Never opens an audio capture stream.</summary>
internal sealed class MicrophoneActivity : IDisposable
{
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    sealed class Listener : IMMNotificationClient
    {
        readonly MicrophoneActivity _owner;
        public Listener(MicrophoneActivity owner) => _owner = owner;
        public void OnDeviceStateChanged(string id, int state) => _owner.Changed?.Invoke();
        public void OnDeviceAdded(string id) => _owner.Changed?.Invoke();
        public void OnDeviceRemoved(string id) => _owner.Changed?.Invoke();
        public void OnDefaultDeviceChanged(int flow, int role, string? id) => _owner.Changed?.Invoke();
        public void OnPropertyValueChanged(string id, PROPERTYKEY key) { }
    }

    readonly object _gate = new();
    readonly IMMDeviceEnumerator _enumerator;
    readonly Listener _listener;
    readonly Dictionary<string, AppVolumes> _devices = new(StringComparer.OrdinalIgnoreCase);
    bool _disposed;
    public event Action? Changed;

    public MicrophoneActivity()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        _listener = new Listener(this);
        Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_listener));
    }

    public IReadOnlyList<SensorUse> Read()
    {
        lock (_gate)
        {
            if (_disposed) return Array.Empty<SensorUse>();
            Marshal.ThrowExceptionForHR(_enumerator.EnumAudioEndpoints(1 /* eCapture */, 1 /* DEVICE_STATE_ACTIVE */, out var endpoints));
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                Marshal.ThrowExceptionForHR(endpoints.GetCount(out int count));
                for (int i = 0; i < count; i++)
                {
                    if (endpoints.Item(i, out var device) != 0) continue;
                    try
                    {
                        if (device.GetId(out string id) != 0) continue;
                        present.Add(id);
                        if (_devices.ContainsKey(id)) continue;
                        var sessions = new AppVolumes(id);
                        sessions.Changed += OnChanged;
                        _devices.Add(id, sessions);
                    }
                    finally { Marshal.ReleaseComObject(device); }
                }
            }
            finally { Marshal.ReleaseComObject(endpoints); }
            foreach (string removed in _devices.Keys.Where(id => !present.Contains(id)).ToArray())
            {
                _devices[removed].Dispose();
                _devices.Remove(removed);
            }
            return Active(_devices.Values.SelectMany(device => device.ReadSessions()), DateTime.UtcNow);
        }
    }

    internal static IReadOnlyList<SensorUse> Active(IEnumerable<AudioSessionInfo> sessions, DateTime now)
        => sessions.Where(s => s.State == AudioSessionInfo.Active && !s.IsSystemSounds && s.ProcessId != 0 && s.ExePath.Length > 0)
            .Select(s => Path.GetFileNameWithoutExtension(s.ExePath))
            .Where(key => key.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => new SensorUse(Sensor.Microphone, PrivacyMonitor.NameOfProcess(key), key, now)).ToArray();

    void OnChanged() => Changed?.Invoke();

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _enumerator.UnregisterEndpointNotificationCallback(_listener);
            foreach (var device in _devices.Values) device.Dispose();
            _devices.Clear();
            Marshal.ReleaseComObject(_enumerator);
        }
    }
}
