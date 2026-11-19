using System;

namespace Holm.Platform;

/// <summary>
/// Whether the computer reaches the internet, told by the system the moment it changes (Windows: the Network List
/// Manager; Linux: NetworkManager). Nothing is pinged or polled.
/// </summary>
public interface IConnectivityPlatform
{
    /// <summary>The internet came or went; raised on any thread, only on a real change.</summary>
    event Action<bool>? Changed;

    /// <summary>Null while unknown (the system hasn't said yet, or can't).</summary>
    bool? Online { get; }

    void Start();
    void Stop();

    /// <summary>The connection to the internet now: Wi-Fi (name, bars of 5) or Ethernet, and its link speed. Null when unknown.</summary>
    NetworkLinkInfo? CurrentLink();
}

/// <param name="AdapterId">The adapter, as <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/> knows it.</param>
public sealed record NetworkLinkInfo(bool IsWifi, string? Ssid, int? SignalBars, ulong LinkBitsPerSecond, string? AdapterId);

/// <summary>For systems without a backend yet: never knows.</summary>
public sealed class NoConnectivityPlatform : IConnectivityPlatform
{
    public event Action<bool>? Changed { add { } remove { } }
    public bool? Online => null;
    public void Start() { }
    public void Stop() { }
    public NetworkLinkInfo? CurrentLink() => null;
}
