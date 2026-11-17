using System.Threading;
using System.Threading.Tasks;

namespace Holm.Platform;

/// <summary>One-time approximate location lookup for initial weather setup (rounded before storage or request).</summary>
public interface ILocationPlatform
{
    bool IsSupported { get; }
    Task<(string Label, double Latitude, double Longitude)?> TryGetApproximateAsync(CancellationToken cancel);
}

public sealed class NoLocationPlatform : ILocationPlatform
{
    public bool IsSupported => false;
    public Task<(string Label, double Latitude, double Longitude)?> TryGetApproximateAsync(CancellationToken cancel)
        => Task.FromResult<(string Label, double Latitude, double Longitude)?>(null);
}
