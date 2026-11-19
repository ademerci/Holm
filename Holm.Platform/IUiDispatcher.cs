using System;

namespace Holm.Platform;

/// <summary>
/// Runs work on the UI thread. Shared code takes this instead of the WPF Dispatcher, so the same service
/// works under WPF (main) and Avalonia (rewrite).
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action);
}
