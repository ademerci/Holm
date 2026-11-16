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
}
