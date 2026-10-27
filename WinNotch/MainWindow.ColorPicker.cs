using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The color picker: the button in the open notch closes it, the next click anywhere picks that pixel's color,
/// its hex code goes on the clipboard and the notch shows the color for a moment.
/// </summary>
public partial class MainWindow
{
    const string SwatchGlyph = ""; // a filled circle
    ColorPicker? _picker;

    void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        if (_picker != null) return;
        SetExpanded(false);
        // Once the notch has closed, so you can pick what was under it too.
        Dispatcher.InvokeAsync(StartColorPicker, DispatcherPriority.ApplicationIdle);
    }

    void StartColorPicker()
    {
        if (_picker != null) return;
        var picker = new ColorPicker();
        picker.Done += (hex, color) =>
        {
            _picker = null;
            if (hex == null) return;
            ShowPickedColor(TrySetClipboard(hex) ? hex : hex + " (clipboard busy)", color);
        };
        try
        {
            _picker = picker;
            picker.Start();
        }
        catch (Win32Exception ex)
        {
            _picker = null;
            picker.Dispose();
            Diag.Log("Color picker: " + ex.Message);
            ShowPeek(SwatchGlyph, "Can't pick colors right now", "", null, false, null);
        }
    }

    void ShowPickedColor(string hex, Color color)
        => ShowPeek(SwatchGlyph, hex, "copied", null, false, DimBrush, seconds: 4, glyphBrush: Frozen(color));

    void MenuColorPicker_Click(object sender, RoutedEventArgs e)
    {
        _settings.ColorPicker = MenuColorPicker.IsChecked;
        _settings.Save();
        RefreshToolBar();
    }
}
