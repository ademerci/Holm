using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Holm.Platform;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// The battery, read from the kernel (/sys/class/power_supply). UPower says when something about it changed
/// (plugged in, unplugged, another percent); nothing is polled. Without UPower the state is still right each
/// time it is read, only nobody announces a change.
/// </summary>
public sealed class LinuxPowerPlatform : IPowerPlatform
{
    readonly string _folder;
    readonly Bus _bus;
    IDisposable? _watch;
    bool _started;

    public event Action? Changed;

    public LinuxPowerPlatform() : this("/sys/class/power_supply", Bus.System) { }

    internal LinuxPowerPlatform(string folder, Bus bus)
    {
        _folder = folder;
        _bus = bus;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = WatchAsync();
    }

    async Task WatchAsync()
    {
        var watch = await _bus.WatchAsync(new MatchRule
        {
            Type = MessageType.Signal, PathNamespace = "/org/freedesktop/UPower",
            Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged",
        }, _ => Changed?.Invoke()).ConfigureAwait(false);
        if (_started) _watch = watch; else watch?.Dispose();
    }

    public void Stop()
    {
        _started = false;
        _watch?.Dispose();
        _watch = null;
    }

    public PowerState Read()
    {
        try
        {
            if (!Directory.Exists(_folder)) return default;
            int batteries = 0, percentSum = 0;
            bool charging = false, discharging = false, mainsSeen = false, mainsOnline = false;
            foreach (var supply in Directory.EnumerateDirectories(_folder))
            {
                string type = Line(supply, "type");
                if (type == "Battery")
                {
                    // A mouse or a headset shows up here too, marked as a device's own battery.
                    if (Line(supply, "scope") == "Device") continue;
                    if (!int.TryParse(Line(supply, "capacity"), out int percent)) continue;
                    batteries++;
                    percentSum += Math.Clamp(percent, 0, 100);
                    string status = Line(supply, "status");
                    charging |= status == "Charging";
                    discharging |= status == "Discharging";
                }
                else if (type is "Mains" or "USB" or "USB_C" or "USB_PD" or "Wireless")
                {
                    mainsSeen = true;
                    mainsOnline |= Line(supply, "online") == "1";
                }
            }
            if (batteries == 0) return default;
            bool onAc = mainsSeen ? mainsOnline : !discharging;
            return new PowerState(true, (int)Math.Round((double)percentSum / batteries, MidpointRounding.AwayFromZero), onAc, charging);
        }
        catch { return default; }
    }

    static string Line(string folder, string file)
    {
        try { return File.ReadAllText(Path.Combine(folder, file)).Trim(); }
        catch { return ""; }
    }
}

/// <summary>
/// Bluetooth devices and their batteries as BlueZ knows them: one question to the system bus, answered from
/// what BlueZ already has. It never makes the radio scan.
/// </summary>
public sealed class LinuxBluetoothPlatform : IBluetoothPlatform
{
    readonly Bus _bus;

    public LinuxBluetoothPlatform() : this(Bus.System) { }
    internal LinuxBluetoothPlatform(Bus bus) => _bus = bus;

    public List<BluetoothDeviceState> ScanBatteries()
    {
        var found = new List<BluetoothDeviceState>();
        // Called off the UI thread (see IBluetoothPlatform), so waiting here blocks nothing.
        var objects = _bus.CallAsync("org.bluez", "/", "org.freedesktop.DBus.ObjectManager", "GetManagedObjects", ReadObjects).GetAwaiter().GetResult();
        if (objects == null) return found;

        foreach (var interfaces in objects.Values)
        {
            if (!interfaces.TryGetValue("org.bluez.Device1", out var device)) continue;
            // Only devices whose battery BlueZ knows, like the list on Windows.
            if (!interfaces.TryGetValue("org.bluez.Battery1", out var battery)) continue;
            if (!battery.TryGetValue("Percentage", out var percentage) || LinuxMediaPlatform.Number(percentage) is not { } percent) continue;

            string address = Text(device, "Address");
            string name = Text(device, "Alias");
            if (name.Length == 0) name = Text(device, "Name");
            if (address.Length == 0 || name.Length == 0) continue;
            bool connected = device.TryGetValue("Connected", out var c) && c.Type == VariantValueType.Bool && c.GetBool();
            found.Add(new BluetoothDeviceState(address, name, GlyphFor(Text(device, "Icon")), Math.Clamp((int)percent, 0, 100), connected));
        }
        found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    /// <summary>BlueZ names the kind of device with an icon name from the freedesktop naming specification.</summary>
    internal static string GlyphFor(string icon) => icon switch
    {
        "audio-headset" or "audio-headphones" or "audio-card" or "audio-speakers" => DeviceGlyphs.Headphones,
        "input-mouse" => DeviceGlyphs.Mouse,
        "input-keyboard" => DeviceGlyphs.Keyboard,
        "input-gaming" => DeviceGlyphs.Controller,
        "input-tablet" => DeviceGlyphs.Pen,
        "phone" => DeviceGlyphs.Phone,
        "computer" => DeviceGlyphs.Computer,
        _ => DeviceGlyphs.Other,
    };

    static string Text(Dictionary<string, VariantValue> values, string key)
        => values.TryGetValue(key, out var v) ? LinuxMediaPlatform.Text(v) : "";

    /// <summary>The answer of GetManagedObjects: every object with its interfaces and their properties.</summary>
    static Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>> ReadObjects(Message message, object? state)
    {
        var objects = new Dictionary<string, Dictionary<string, Dictionary<string, VariantValue>>>();
        var reader = message.GetBodyReader();
        ArrayEnd objectsEnd = reader.ReadDictionaryStart();
        while (reader.HasNext(objectsEnd))
        {
            string path = reader.ReadObjectPathAsString();
            var interfaces = new Dictionary<string, Dictionary<string, VariantValue>>();
            ArrayEnd interfacesEnd = reader.ReadDictionaryStart();
            while (reader.HasNext(interfacesEnd))
            {
                string name = reader.ReadString();
                interfaces[name] = reader.ReadDictionaryOfStringToVariantValue();
            }
            objects[path] = interfaces;
        }
        return objects;
    }
}
