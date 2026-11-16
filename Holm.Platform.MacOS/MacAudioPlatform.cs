using System;
using System.Runtime.InteropServices;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// The default output's volume through CoreAudio. CoreAudio calls back when the volume, the mute or the default
/// device changes; nothing is polled.
///
/// Devices differ in how they expose their volume: most have the virtual main volume the menu bar uses, some only a
/// main volume, some only one volume per channel. The first that works is used and remembered per device.
///
/// The equalizer can't follow the sound here: reading what plays needs a recording permission on macOS (screen
/// or audio capture), which a notch should not ask for. <see cref="MeterWorking"/> stays false, and the bars bounce
/// as they do on the other systems when the sound can't be read.
/// </summary>
public sealed unsafe class MacAudioPlatform : IAudioPlatform
{
    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    const uint SystemObject = 1;
    static readonly uint DefaultOutput = Code("dOut"), Global = Code("glob"), Output = Code("outp"),
        VirtualMainVolume = Code("vmvc"), VolumeScalar = Code("volm"), Mute = Code("mute");

    enum Route { Unknown, VirtualMain, VirtualMainService, MainElement, Channels, None }

    static MacAudioPlatform? _current;
    readonly object _gate = new();
    uint _device;
    Route _route;
    bool _listening;

    public event Action<float, bool>? VolumeChanged;
    public event Action? DefaultDeviceChanged;

    public void Start()
    {
        lock (_gate)
        {
            _current = this;
            if (!_listening)
            {
                var address = new PropertyAddress(DefaultOutput, Global, 0);
                Try(() => AudioObjectAddPropertyListener(SystemObject, ref address, &OnSystemChanged, IntPtr.Zero));
                _listening = true;
            }
            RemoveDeviceListeners();
            _device = ReadDefaultDevice();
            _route = Route.Unknown;
            AddDeviceListeners();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            RemoveDeviceListeners();
            if (_listening)
            {
                var address = new PropertyAddress(DefaultOutput, Global, 0);
                Try(() => AudioObjectRemovePropertyListener(SystemObject, ref address, &OnSystemChanged, IntPtr.Zero));
                _listening = false;
            }
            _device = 0;
        }
    }

    public bool TryGetVolume(out float level, out bool muted)
    {
        level = 0;
        muted = false;
        lock (_gate)
        {
            if (_device == 0) return false;
            if (!ReadVolume(_device, ref _route, out level)) return false;
            muted = ReadMute(_device);
            return true;
        }
    }

    public void SetVolume(float level)
    {
        level = Math.Clamp(level, 0f, 1f);
        lock (_gate)
        {
            if (_device == 0) return;
            if (_route == Route.Unknown) ReadVolume(_device, ref _route, out _);
            switch (_route)
            {
                case Route.VirtualMain: SetFloat(_device, VirtualMainVolume, 0, level); break;
                case Route.VirtualMainService: SetFloatService(_device, level); break;
                case Route.MainElement: SetFloat(_device, VolumeScalar, 0, level); break;
                case Route.Channels: SetFloat(_device, VolumeScalar, 1, level); SetFloat(_device, VolumeScalar, 2, level); break;
            }
            // Turning the volume up from silence unmutes, as the volume keys do.
            if (level > 0 && ReadMute(_device)) SetMuteLocked(false);
        }
    }

    public void SetMute(bool mute)
    {
        lock (_gate) SetMuteLocked(mute);
    }

    public void ToggleMute()
    {
        lock (_gate) if (_device != 0) SetMuteLocked(!ReadMute(_device));
    }

    void SetMuteLocked(bool mute)
    {
        if (_device == 0) return;
        uint value = mute ? 1u : 0u;
        var address = new PropertyAddress(Mute, Output, 0);
        AudioObjectSetPropertyData(_device, ref address, 0, IntPtr.Zero, sizeof(uint), &value);
    }

    public void StartMeter() { }
    public void StopMeter() { }
    public bool MeterWorking => false;
    public float MeterBar(int bar, int bars) => 0;

    static uint ReadDefaultDevice()
    {
        uint device = 0, size = sizeof(uint);
        var address = new PropertyAddress(DefaultOutput, Global, 0);
        return AudioObjectGetPropertyData(SystemObject, ref address, 0, IntPtr.Zero, ref size, &device) == 0 ? device : 0;
    }

    /// <summary>Reads the volume the way that works for this device, finding it out the first time.</summary>
    static bool ReadVolume(uint device, ref Route route, out float level)
    {
        level = 0;
        if (route is Route.Unknown or Route.VirtualMain && GetFloat(device, VirtualMainVolume, 0, out level)) { route = Route.VirtualMain; return true; }
        if (route is Route.Unknown or Route.VirtualMainService && GetFloatService(device, out level)) { route = Route.VirtualMainService; return true; }
        if (route is Route.Unknown or Route.MainElement && GetFloat(device, VolumeScalar, 0, out level)) { route = Route.MainElement; return true; }
        if (route is Route.Unknown or Route.Channels && GetFloat(device, VolumeScalar, 1, out float left))
        {
            level = GetFloat(device, VolumeScalar, 2, out float right) ? Math.Max(left, right) : left;
            route = Route.Channels;
            return true;
        }
        if (route == Route.Unknown) route = Route.None;
        return false;
    }

    static bool ReadMute(uint device)
    {
        uint value = 0, size = sizeof(uint);
        var address = new PropertyAddress(Mute, Output, 0);
        return AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, &value) == 0 && value != 0;
    }

    static bool GetFloat(uint device, uint selector, uint element, out float value)
    {
        float v = 0;
        uint size = sizeof(float);
        var address = new PropertyAddress(selector, Output, element);
        bool ok = AudioObjectHasProperty(device, ref address) != 0
               && AudioObjectGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, &v) == 0;
        value = Math.Clamp(v, 0f, 1f);
        return ok;
    }

    static void SetFloat(uint device, uint selector, uint element, float value)
    {
        var address = new PropertyAddress(selector, Output, element);
        AudioObjectSetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(float), &value);
    }

    /// <summary>The virtual main volume through AudioToolbox, for systems where CoreAudio doesn't answer it directly.</summary>
    static bool GetFloatService(uint device, out float value)
    {
        float v = 0;
        uint size = sizeof(float);
        var address = new PropertyAddress(VirtualMainVolume, Output, 0);
        bool ok;
        try { ok = AudioHardwareServiceGetPropertyData(device, ref address, 0, IntPtr.Zero, ref size, &v) == 0; }
        catch (EntryPointNotFoundException) { ok = false; }
        value = Math.Clamp(v, 0f, 1f);
        return ok;
    }

    static void SetFloatService(uint device, float value)
    {
        var address = new PropertyAddress(VirtualMainVolume, Output, 0);
        try { AudioHardwareServiceSetPropertyData(device, ref address, 0, IntPtr.Zero, sizeof(float), &value); }
        catch (EntryPointNotFoundException) { }
    }

    static readonly (uint Selector, uint Element)[] Watched =
        { (VirtualMainVolume, 0), (VolumeScalar, 0), (VolumeScalar, 1), (VolumeScalar, 2), (Mute, 0) };

    void AddDeviceListeners()
    {
        if (_device == 0) return;
        foreach (var (selector, element) in Watched)
        {
            var address = new PropertyAddress(selector, Output, element);
            if (AudioObjectHasProperty(_device, ref address) == 0) continue;
            AudioObjectAddPropertyListener(_device, ref address, &OnDeviceChanged, IntPtr.Zero);
        }
    }

    void RemoveDeviceListeners()
    {
        if (_device == 0) return;
        foreach (var (selector, element) in Watched)
        {
            var address = new PropertyAddress(selector, Output, element);
            AudioObjectRemovePropertyListener(_device, ref address, &OnDeviceChanged, IntPtr.Zero);
        }
    }

    [UnmanagedCallersOnly]
    static int OnDeviceChanged(uint objectId, uint count, PropertyAddress* addresses, IntPtr clientData)
    {
        try
        {
            if (_current is { } audio && audio.TryGetVolume(out float level, out bool muted)) audio.VolumeChanged?.Invoke(level, muted);
        }
        catch { }
        return 0;
    }

    [UnmanagedCallersOnly]
    static int OnSystemChanged(uint objectId, uint count, PropertyAddress* addresses, IntPtr clientData)
    {
        try { _current?.DefaultDeviceChanged?.Invoke(); } catch { }
        return 0;
    }

    static void Try(Func<int> call)
    {
        try { call(); } catch { }
    }

    /// <summary>CoreAudio's four-character codes, as the big-endian number they stand for.</summary>
    internal static uint Code(string fourCharacters)
        => (uint)fourCharacters[0] << 24 | (uint)fourCharacters[1] << 16 | (uint)fourCharacters[2] << 8 | fourCharacters[3];

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct PropertyAddress
    {
        public readonly uint Selector, Scope, Element;
        public PropertyAddress(uint selector, uint scope, uint element) => (Selector, Scope, Element) = (selector, scope, element);
    }

    [DllImport(CoreAudio)] static extern byte AudioObjectHasProperty(uint objectId, ref PropertyAddress address);
    [DllImport(CoreAudio)] static extern int AudioObjectGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size, void* data);
    [DllImport(CoreAudio)] static extern int AudioObjectSetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint size, void* data);
    [DllImport(CoreAudio)] static extern int AudioObjectAddPropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);
    [DllImport(CoreAudio)] static extern int AudioObjectRemovePropertyListener(uint objectId, ref PropertyAddress address, delegate* unmanaged<uint, uint, PropertyAddress*, IntPtr, int> listener, IntPtr clientData);
    [DllImport(AudioToolbox)] static extern int AudioHardwareServiceGetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint size, void* data);
    [DllImport(AudioToolbox)] static extern int AudioHardwareServiceSetPropertyData(uint objectId, ref PropertyAddress address, uint qualifierSize, IntPtr qualifier, uint size, void* data);
}
