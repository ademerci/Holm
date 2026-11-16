using System;
using System.Collections.Generic;

namespace WinNotch;

/// <summary>The web browsers whose tab strip runs along the top of the screen, by process name.</summary>
internal static class Browsers
{
    static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "brave", "opera", "opera_gx", "vivaldi", "librewolf", "waterfox", "floorp", "zen",
        "thorium", "chromium", "arc", "iexplore",
    };

    public static bool Is(string processName) => processName.Length > 0 && Names.Contains(processName);
}
