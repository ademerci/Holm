using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Holm.Platform;
using Tmds.DBus.Protocol;

namespace Holm.Platform.Linux;

/// <summary>
/// Internet connectivity as NetworkManager judges it (its Connectivity property: "full" is the internet, a captive
/// portal or a local network only is not), announced by NetworkManager whenever it changes. Without NetworkManager
/// the kernel's view stands in: whether any network is up at all.
/// </summary>
public sealed class LinuxConnectivityPlatform : IConnectivityPlatform
{
    const string Service = "org.freedesktop.NetworkManager", Path = "/org/freedesktop/NetworkManager";
    const uint ConnectivityFull = 4;

    readonly Bus _bus;
    IDisposable? _watch;
    bool _started, _fallback;
    volatile int _state = -1; // -1 unknown, 0 offline, 1 online

    public event Action<bool>? Changed;

    public bool? Online => _state < 0 ? null : _state == 1;

    public LinuxConnectivityPlatform() : this(Bus.System) { }
    internal LinuxConnectivityPlatform(Bus bus) => _bus = bus;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _ = StartAsync();
    }

    async Task StartAsync()
    {
        var value = await _bus.GetAsync(Service, Path, Service, "Connectivity").ConfigureAwait(false);
        if (value is not { } connectivity || connectivity.Type != VariantValueType.UInt32)
        {
            // No NetworkManager: whether any network is up.
            _fallback = true;
            NetworkChange.NetworkAvailabilityChanged += OnAvailability;
            Report(NetworkInterface.GetIsNetworkAvailable());
            return;
        }
        Report(connectivity.GetUInt32() == ConnectivityFull);
        var watch = await _bus.WatchAsync(new MatchRule
        {
            Type = MessageType.Signal, Path = Path, Interface = "org.freedesktop.DBus.Properties", Member = "PropertiesChanged", Arg0 = Service,
        }, signal => { _ = RereadAsync(); }).ConfigureAwait(false);
        if (_started) _watch = watch; else watch?.Dispose();
    }

    async Task RereadAsync()
    {
        if (await _bus.GetAsync(Service, Path, Service, "Connectivity").ConfigureAwait(false) is { Type: VariantValueType.UInt32 } value)
            Report(value.GetUInt32() == ConnectivityFull);
    }

    void OnAvailability(object? sender, NetworkAvailabilityEventArgs e) => Report(e.IsAvailable);

    void Report(bool online)
    {
        int state = online ? 1 : 0;
        int before = _state;
        _state = state;
        if (before >= 0 && before != state) Changed?.Invoke(online);
    }

    public void Stop()
    {
        _started = false;
        _watch?.Dispose();
        _watch = null;
        if (_fallback) NetworkChange.NetworkAvailabilityChanged -= OnAvailability;
        _fallback = false;
        _state = -1;
    }

    /// <summary>
    /// The connection that carries the default route: Wi-Fi when the kernel calls it wireless, with the network's
    /// name, signal and bit rate from NetworkManager (nmcli), else Ethernet with the speed the driver reports.
    /// </summary>
    public NetworkLinkInfo? CurrentLink()
    {
        try
        {
            string? device = DefaultRouteDevice();
            if (device == null) return null;
            string id = device;
            if (!System.IO.Directory.Exists($"/sys/class/net/{device}/wireless"))
            {
                ulong speed = 0;
                try { if (ulong.TryParse(System.IO.File.ReadAllText($"/sys/class/net/{device}/speed").Trim(), out ulong mbps)) speed = mbps * 1_000_000; }
                catch (System.IO.IOException) { }
                return new NetworkLinkInfo(false, null, null, speed, id);
            }
            string? ssid = null; int? bars = null; ulong bits = 0;
            foreach (string line in Run("nmcli", "-t", "-f", "ACTIVE,SSID,SIGNAL,RATE", "device", "wifi", "list", "ifname", device).Split('\n'))
            {
                // yes:Home:72:540 Mbit/s (colons in the name come escaped as \:)
                var fields = System.Text.RegularExpressions.Regex.Split(line, @"(?<!\\):");
                if (fields.Length < 4 || fields[0] != "yes") continue;
                ssid = fields[1].Replace("\\:", ":");
                if (int.TryParse(fields[2], out int signal)) bars = Math.Clamp((signal + 19) / 20, 0, 5);
                if (ulong.TryParse(fields[3].Split(' ')[0], out ulong rate)) bits = rate * 1_000_000;
                break;
            }
            return new NetworkLinkInfo(true, ssid, bars, bits, id);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException) { return null; }
    }

    /// <summary>The interface of the default route (/proc/net/route: destination 00000000).</summary>
    static string? DefaultRouteDevice()
    {
        foreach (string line in System.IO.File.ReadLines("/proc/net/route"))
        {
            var fields = line.Split('\t');
            if (fields.Length > 1 && fields[1] == "00000000") return fields[0];
        }
        return null;
    }

    static string Run(string program, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo(program) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        if (process == null) return "";
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(3000);
        return output;
    }
}
