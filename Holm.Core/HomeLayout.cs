using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch;

/// <summary>
/// The open notch's start page as you arrange it (Settings › Start page): its sections from top to bottom, the ones
/// you hid, the ones with their two sides swapped, and the order of the tools in its bar. The settings keep it as
/// lists of names that stay empty for Holm's usual layout, so the usual layout is the default. A section or tool a
/// later version adds shows at its usual place; a name this version doesn't know is left out.
/// </summary>
internal sealed class HomeLayout
{
    // The sections.
    public const string Tools = "tools", Main = "main", Today = "today", Live = "live", Shelf = "shelf", Status = "status";

    // The tools in the bar.
    public const string Notes = "notes", Reminders = "reminders", Calculator = "calculator", Chats = "chats",
        Clipboard = "clipboard", Mixer = "mixer", Ai = "ai", ColorPicker = "color", Beam = "beam", Phone = "phone";

    /// <summary>The sections from top to bottom as Holm shows them unless you move them.</summary>
    public static readonly IReadOnlyList<string> UsualSections = new[] { Tools, Main, Today, Live, Shelf, Status };

    /// <summary>The tools from left to right as Holm shows them unless you move them.</summary>
    public static readonly IReadOnlyList<string> UsualTools = new[] { Notes, Reminders, Calculator, Chats, Clipboard, Mixer, Ai, ColorPicker, Beam, Phone };

    /// <summary>
    /// What you can hide: the tool bar, the two cards, and the battery and volume. The player and clock always shows;
    /// the activity and the shelf show only while there is something in them, and their parts have their own switches.
    /// </summary>
    public static readonly IReadOnlyList<string> CanHide = new[] { Tools, Today, Status };

    /// <summary>What has two sides that can swap: the clock and the weather (the cover and the title), the two cards, the batteries and the volume.</summary>
    public static readonly IReadOnlyList<string> CanSwap = new[] { Main, Today, Status };

    public static readonly HomeLayout Usual = new(UsualSections, Array.Empty<string>(), Array.Empty<string>(), UsualTools, Array.Empty<string>());

    /// <summary>Every section once, top to bottom.</summary>
    public IReadOnlyList<string> Sections { get; }

    /// <summary>The hidden sections, in their usual order.</summary>
    public IReadOnlyList<string> Hidden { get; }

    /// <summary>The sections with their sides swapped, in their usual order.</summary>
    public IReadOnlyList<string> Swapped { get; }

    /// <summary>Every tool once, left to right.</summary>
    public IReadOnlyList<string> ToolOrder { get; }

    /// <summary>The hidden tools, in their usual order.</summary>
    public IReadOnlyList<string> HiddenTools { get; }

    HomeLayout(IReadOnlyList<string> sections, IReadOnlyList<string> hidden, IReadOnlyList<string> swapped,
               IReadOnlyList<string> tools, IReadOnlyList<string> hiddenTools)
    {
        Sections = sections;
        Hidden = hidden;
        Swapped = swapped;
        ToolOrder = tools;
        HiddenTools = hiddenTools;
    }

    public bool IsHidden(string section) => Hidden.Contains(section);

    public bool IsSwapped(string section) => Swapped.Contains(section);
    public bool IsToolHidden(string tool) => HiddenTools.Contains(tool);

    /// <summary>Holm's usual start page: nothing moved, hidden or swapped.</summary>
    public bool IsUsual => Sections.SequenceEqual(UsualSections) && Hidden.Count == 0 && Swapped.Count == 0
        && ToolOrder.SequenceEqual(UsualTools) && HiddenTools.Count == 0;

    /// <summary>The layout the settings hold, made whole.</summary>
    public static HomeLayout Read(AppSettings settings) => new(
        Arrange(settings.HomeSections, UsualSections), Only(settings.HomeHidden, CanHide),
        Only(settings.HomeSwapped, CanSwap), Arrange(settings.HomeTools, UsualTools), Only(settings.HomeHiddenTools, UsualTools));

    /// <summary>Puts the layout into the settings; a usual order is saved as an empty list.</summary>
    public void Write(AppSettings settings)
    {
        settings.HomeSections = Sections.SequenceEqual(UsualSections) ? new() : Sections.ToList();
        settings.HomeHidden = Hidden.ToList();
        settings.HomeSwapped = Swapped.ToList();
        settings.HomeTools = ToolOrder.SequenceEqual(UsualTools) ? new() : ToolOrder.ToList();
        settings.HomeHiddenTools = HiddenTools.ToList();
    }

    /// <summary>The layout with <paramref name="section"/> moved to <paramref name="index"/> (counted without it).</summary>
    public HomeLayout Move(string section, int index) => new(MoveTo(Sections, section, index), Hidden, Swapped, ToolOrder, HiddenTools);

    /// <summary>The layout with <paramref name="tool"/> moved to <paramref name="index"/> in the bar (counted without it).</summary>
    public HomeLayout MoveTool(string tool, int index) => new(Sections, Hidden, Swapped, MoveTo(ToolOrder, tool, index), HiddenTools);

    /// <summary>The layout with a tool hidden or shown, keeping its place in the bar.</summary>
    public HomeLayout HideTool(string tool, bool hide) => new(Sections, Hidden, Swapped, ToolOrder, Set(HiddenTools, tool, hide, UsualTools));

    /// <summary>The layout with <paramref name="section"/> hidden or shown; a section that can't hide stays shown.</summary>
    public HomeLayout Hide(string section, bool hide) => new(Sections, Set(Hidden, section, hide, CanHide), Swapped, ToolOrder, HiddenTools);

    /// <summary>The layout with <paramref name="section"/>'s sides swapped or not.</summary>
    public HomeLayout Swap(string section, bool swap) => new(Sections, Hidden, Set(Swapped, section, swap, CanSwap), ToolOrder, HiddenTools);

    /// <summary>
    /// A saved order made whole: the names it knows, once each and in its order, and every name it misses right after
    /// the one that comes before it in <paramref name="usual"/> (first, if none does).
    /// </summary>
    public static List<string> Arrange(IEnumerable<string>? saved, IReadOnlyList<string> usual)
    {
        var order = new List<string>();
        foreach (string name in saved ?? Array.Empty<string>())
            if (usual.Contains(name) && !order.Contains(name)) order.Add(name);
        for (int i = 0; i < usual.Count; i++)
            if (!order.Contains(usual[i])) order.Insert(i == 0 ? 0 : order.IndexOf(usual[i - 1]) + 1, usual[i]);
        return order;
    }

    /// <summary>The saved names that are allowed, once each, in the order they are allowed in.</summary>
    static string[] Only(IEnumerable<string>? saved, IReadOnlyList<string> allowed)
    {
        var names = saved?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        return allowed.Where(names.Contains).ToArray();
    }

    static string[] Set(IReadOnlyList<string> names, string name, bool on, IReadOnlyList<string> allowed)
        => Only(on ? names.Append(name) : names.Where(n => n != name), allowed);

    static string[] MoveTo(IReadOnlyList<string> order, string name, int index)
    {
        if (!order.Contains(name)) return order.ToArray();
        var moved = order.Where(n => n != name).ToList();
        moved.Insert(Math.Clamp(index, 0, moved.Count), name);
        return moved.ToArray();
    }
}
