using System;
using Windows.Networking.Connectivity;

namespace WinNotch;

/// <summary>The Windows side of the speed test: the connection as Windows reports it. The rest is in SpeedTest.cs.</summary>
internal static partial class SpeedTest
{
    /// <summary>
    /// The connection to the internet, as Windows reports it. The Wi-Fi name may be missing when Windows' location
    /// permission is off for desktop apps (Windows 11 24H2 and later).
    /// </summary>
    public static NetworkLink? CurrentLink()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null) return null;
            ulong bits = profile.NetworkAdapter?.InboundMaxBitsPerSecond ?? 0;
            Guid? adapter = profile.NetworkAdapter?.NetworkAdapterId;
            if (!profile.IsWlanConnectionProfile) return new NetworkLink(false, null, null, bits, adapter);
            string? ssid = null;
            try { ssid = profile.WlanConnectionProfileDetails?.GetConnectedSsid(); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
            int? signal = profile.GetSignalBars() is { } b ? b : null;
            return new NetworkLink(true, ssid, signal, bits, adapter);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidCastException)
        {
            Diag.Log("Network link: " + ex.GetType().Name);
            return null;
        }
    }
}
