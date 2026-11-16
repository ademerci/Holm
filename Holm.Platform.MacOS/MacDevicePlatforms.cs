using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Holm.Platform;

namespace Holm.Platform.MacOS;

/// <summary>
/// The battery through IOKit's power sources, as the menu bar shows it. IOKit calls back on the main run loop when
/// anything about a power source changes; nothing is polled.
/// </summary>
public sealed unsafe class MacPowerPlatform : IPowerPlatform
{
    const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";

    static MacPowerPlatform? _current;
    IntPtr _source;

    public event Action? Changed;

    public void Start()
    {
        if (_source != IntPtr.Zero) return;
        _current = this;
        try
        {
            _source = IOPSNotificationCreateRunLoopSource(&OnChanged, IntPtr.Zero);
            if (_source != IntPtr.Zero) CF.CFRunLoopAddSource(CF.CFRunLoopGetMain(), _source, CF.DefaultMode);
        }
        catch { _source = IntPtr.Zero; }
    }

    public void Stop()
    {
        if (_source == IntPtr.Zero) return;
        try
        {
            CF.CFRunLoopRemoveSource(CF.CFRunLoopGetMain(), _source, CF.DefaultMode);
            CF.CFRelease(_source);
        }
        catch { }
        _source = IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    static void OnChanged(IntPtr context)
    {
        try { _current?.Changed?.Invoke(); } catch { }
    }

    public PowerState Read()
    {
        IntPtr blob = IntPtr.Zero, list = IntPtr.Zero;
        try
        {
            blob = IOPSCopyPowerSourcesInfo();
            if (blob == IntPtr.Zero) return default;
            list = IOPSCopyPowerSourcesList(blob);
            if (list == IntPtr.Zero) return default;
            var sources = new List<PowerSource>();
            for (nint i = 0, n = CF.CFArrayGetCount(list); i < n; i++)
            {
                IntPtr description = IOPSGetPowerSourceDescription(blob, CF.CFArrayGetValueAtIndex(list, i));
                if (description == IntPtr.Zero) continue;
                sources.Add(new PowerSource(
                    CF.Text(CF.Get(description, "Type")) ?? "",
                    CF.Bool(CF.Get(description, "Is Present")) ?? true,
                    CF.Number(CF.Get(description, "Current Capacity")),
                    CF.Number(CF.Get(description, "Max Capacity")),
                    CF.Text(CF.Get(description, "Power Source State")) ?? "",
                    CF.Bool(CF.Get(description, "Is Charging")) ?? false));
            }
            return Combine(sources);
        }
        catch { return default; }
        finally
        {
            if (list != IntPtr.Zero) CF.CFRelease(list);
            if (blob != IntPtr.Zero) CF.CFRelease(blob);
        }
    }

    internal readonly record struct PowerSource(string Type, bool Present, double? Current, double? Max, string State, bool Charging);

    /// <summary>The computer's own batteries (a UPS or a mouse shows up here too, under another type).</summary>
    internal static PowerState Combine(IEnumerable<PowerSource> sources)
    {
        int batteries = 0;
        double percentSum = 0;
        bool onAc = false, charging = false;
        foreach (var s in sources)
        {
            if (s.Type != "InternalBattery" || !s.Present || s.Current is not { } current) continue;
            double max = s.Max is > 0 and var m ? m : 100;
            batteries++;
            percentSum += Math.Clamp(current / max * 100, 0, 100);
            onAc |= s.State == "AC Power";
            charging |= s.Charging;
        }
        if (batteries == 0) return default;
        return new PowerState(true, (int)Math.Round(percentSum / batteries, MidpointRounding.AwayFromZero), onAc, charging);
    }

    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesInfo();
    [DllImport(IOKit)] static extern IntPtr IOPSCopyPowerSourcesList(IntPtr blob);
    [DllImport(IOKit)] static extern IntPtr IOPSGetPowerSourceDescription(IntPtr blob, IntPtr source);
    [DllImport(IOKit)] static extern IntPtr IOPSNotificationCreateRunLoopSource(delegate* unmanaged<IntPtr, void> callback, IntPtr context);
}

/// <summary>
/// Bluetooth devices and their batteries as System Information lists them (system_profiler): what macOS already
/// knows, AirPods with each bud, mice, keyboards and trackpads. It never makes the radio scan. Takes a second or
/// so, which is fine: it is called off the UI thread, once the open notch has landed.
/// </summary>
public sealed class MacBluetoothPlatform : IBluetoothPlatform
{
    public List<BluetoothDeviceState> ScanBatteries()
    {
        try
        {
            var start = new ProcessStartInfo("/usr/sbin/system_profiler", "SPBluetoothDataType -json -detailLevel basic")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process == null) return new();
            string json = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000)) { try { process.Kill(); } catch { } return new(); }
            return Parse(json);
        }
        catch { return new(); }
    }

    /// <summary>The devices with a battery in system_profiler's answer, connected ones and remembered ones.</summary>
    internal static List<BluetoothDeviceState> Parse(string json)
    {
        var found = new List<BluetoothDeviceState>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("SPBluetoothDataType", out var controllers) || controllers.ValueKind != JsonValueKind.Array)
                return found;
            foreach (var controller in controllers.EnumerateArray())
            {
                if (controller.ValueKind != JsonValueKind.Object) continue;
                Collect(controller, "device_connected", true, found);
                Collect(controller, "device_not_connected", false, found);
            }
        }
        catch (JsonException) { }
        found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return found;
    }

    /// <summary>A list of one-entry objects: the device's name, then its properties.</summary>
    static void Collect(JsonElement controller, string listName, bool connected, List<BluetoothDeviceState> found)
    {
        if (!controller.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array) return;
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var device in entry.EnumerateObject())
            {
                if (device.Value.ValueKind != JsonValueKind.Object) continue;
                var props = device.Value;
                if (Battery(props) is not { } battery) continue;
                string address = Text(props, "device_address");
                if (address.Length == 0 || device.Name.Length == 0) continue;
                if (found.Exists(d => d.Address == address)) continue;
                found.Add(new BluetoothDeviceState(address, device.Name, GlyphFor(Text(props, "device_minorType")), battery, connected));
            }
        }
    }

    /// <summary>
    /// One number for the device: its own battery, or for earbuds the emptier of the two (the case doesn't count,
    /// as on Windows).
    /// </summary>
    static int? Battery(JsonElement props)
    {
        if (Percent(props, "device_batteryLevelMain") is { } main) return main;
        if (Percent(props, "device_batteryLevel") is { } single) return single;
        int? left = Percent(props, "device_batteryLevelLeft"), right = Percent(props, "device_batteryLevelRight");
        if (left is { } l && right is { } r) return Math.Min(l, r);
        return left ?? right;
    }

    /// <summary>"85%" (or a bare number) as 85.</summary>
    internal static int? Percent(JsonElement props, string key)
    {
        if (!props.TryGetProperty(key, out var value)) return null;
        string text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? "",
            JsonValueKind.Number => value.GetRawText(),
            _ => "",
        };
        text = text.Trim().TrimEnd('%').Trim();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent)
            ? (int)Math.Clamp(Math.Round(percent), 0, 100) : null;
    }

    static string Text(JsonElement props, string key)
        => props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>System Information's word for the kind of device.</summary>
    internal static string GlyphFor(string minorType) => minorType.ToLowerInvariant() switch
    {
        "headphones" or "headset" or "speaker" or "loudspeaker" or "audio" or "earbuds" => DeviceGlyphs.Headphones,
        "mouse" or "trackpad" => DeviceGlyphs.Mouse,
        "keyboard" => DeviceGlyphs.Keyboard,
        "gamepad" or "game controller" or "joystick" => DeviceGlyphs.Controller,
        "pen" or "digitizer" or "tablet" => DeviceGlyphs.Pen,
        "phone" or "smartphone" or "cellular" => DeviceGlyphs.Phone,
        "computer" or "laptop" or "desktop" => DeviceGlyphs.Computer,
        _ => DeviceGlyphs.Other,
    };
}
