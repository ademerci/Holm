using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Holm.App;

/// <summary>
/// Which display the notch sits on (the WPF app's ListScreens and PositionWindow): with more than one, the menu lists
/// them left to right ("Left screen", "Right screen (main display)") and the choice is kept. The notch follows when
/// displays come and go.
/// </summary>
public sealed partial class NotchWindow
{
    readonly Separator _monitorSeparator = new();

    /// <summary>What the settings remember a display by: its name where the system gives one, else its place.</summary>
    static string ScreenKey(Screen screen) => screen.DisplayName is { Length: > 0 } name ? name : $"{screen.Bounds.X},{screen.Bounds.Y}";

    Screen? ChosenScreen()
    {
        var all = Screens.All;
        return all.FirstOrDefault(s => string.Equals(ScreenKey(s), _settings.Monitor, StringComparison.OrdinalIgnoreCase))
            ?? Screens.Primary ?? (all.Count > 0 ? all[0] : null);
    }

    /// <summary>With more than one display, the menu lists them so the notch can be moved.</summary>
    void ListScreens(ContextMenu menu)
    {
        foreach (var old in menu.Items.OfType<MenuItem>().Where(i => i.Tag is Screen).ToList()) menu.Items.Remove(old);
        var screens = Screens.All.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToList();
        _monitorSeparator.IsVisible = screens.Count > 1;
        if (screens.Count < 2) return;
        string[] places = screens.Count == 2 ? new[] { "Left", "Right" }
                        : screens.Count == 3 ? new[] { "Left", "Middle", "Right" }
                        : Array.Empty<string>();
        var chosen = ChosenScreen();
        int at = menu.Items.IndexOf(_monitorSeparator) + 1;
        for (int i = 0; i < screens.Count; i++)
        {
            var screen = screens[i];
            string name = places.Length > 0 ? $"{places[i]} screen" : $"Screen {i + 1}";
            var item = new MenuItem
            {
                Header = screen.IsPrimary ? name + " (main display)" : name, ToggleType = MenuItemToggleType.Radio,
                IsChecked = chosen != null && ScreenKey(screen) == ScreenKey(chosen), Tag = screen,
            };
            item.Click += (_, _) =>
            {
                _settings.Monitor = ScreenKey(screen);
                _settings.Save();
                PositionWindow();
            };
            menu.Items.Insert(at + i, item);
        }
    }
}
