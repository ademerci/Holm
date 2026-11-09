using System;
using System.Collections.Generic;

namespace WinNotch;

/// <summary>The pages above the settings in the Holm window: friends, chat and achievements.</summary>
public partial class MainWindow
{
    /// <param name="demo">For snapshots: made-up friends and messages only.</param>
    IReadOnlyList<HubPage> HubPages(bool demo)
    {
        var pages = new List<HubPage>();
        return pages;
    }
}
