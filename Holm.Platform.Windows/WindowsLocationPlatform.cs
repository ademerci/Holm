using System.Threading;
using System.Threading.Tasks;
using Holm.Platform;
using WinNotch;

namespace Holm.Platform.Windows;

/// <summary>One-time approximate Windows location read via the linked <see cref="WeatherLocation"/>.</summary>
public sealed class WindowsLocationPlatform : ILocationPlatform
{
    public bool IsSupported => true;

    public async Task<(string Label, double Latitude, double Longitude)?> TryGetApproximateAsync(CancellationToken cancel)
    {
        var place = await WeatherLocation.FindAsync(cancel);
        return place == null ? null : (place.Label, place.Latitude, place.Longitude);
    }
}
