using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace WinNotch;

/// <summary>A one-time Windows location read, rounded before any storage or forecast request.</summary>
internal static class WeatherLocation
{
    public static async Task<WeatherPlace?> FindAsync(CancellationToken cancel)
    {
        var locator = new Geolocator { DesiredAccuracyInMeters = 10_000 };
        locator.AllowFallbackToConsentlessPositions();
        var position = await locator.GetGeopositionAsync(TimeSpan.FromHours(24), TimeSpan.FromSeconds(10)).AsTask(cancel);
        var point = position.Coordinate.Point.Position;
        return Approximate(point.Latitude, point.Longitude);
    }

    internal static WeatherPlace? Approximate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180) return null;
        return new WeatherPlace("Approximate location", "Approximate location", Math.Round(latitude, 1), Math.Round(longitude, 1));
    }
}
