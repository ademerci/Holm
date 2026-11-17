using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Holm.App;

/// <summary>The look of the Holm window (settings, friends, chat, achievements): dark and rounded, or black and square in the instrument look, like the notch (the WPF app's HubTheme).</summary>
internal static class HubTheme
{
    static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();

    // The classic look, or the instrument look with the "Eis" palette: black panels, grey lines, white as the only signal colour.
    public static readonly IBrush Panel = B(Look.Pick(0xFF0B0B0Cu, 0xFA1E1E20u)), Sidebar = B(Look.Pick(0xFF000000u, 0xFF171719u)),
        Edge = B(Look.Pick(0xFF26272Au, 0x2EFFFFFFu)), Card = B(Look.Pick(0xFF141416u, 0x14FFFFFFu)),
        CardStrong = B(Look.Pick(0xFF1C1D20u, 0x1FFFFFFFu)), Hover = B(Look.Pick(0xFF121214u, 0x10FFFFFFu)),
        Selected = B(Look.Pick(0xFF1C1D20u, 0x26FFFFFFu)), Line = B(Look.Pick(0xFF26272Au, 0x14FFFFFFu)),
        Dim = B(Look.Pick(0xFF8A8C91u, 0x99FFFFFFu)), Faint = B(Look.Pick(0xFF5C5E63u, 0x66FFFFFFu)),
        Accent = B(Look.Pick(0xFFF2F2F2u, 0xFF0A84FFu)), Green = B(0xFF30D158), Red = B(0xFFFF453A), Gold = B(0xFFFFC43D);
    /// <summary>Text and glyphs on <see cref="Accent"/>.</summary>
    public static readonly IBrush OnAccent = Look.Pick<IBrush>(B(0xFF0B0B0C), Brushes.White);

    public static FontFamily Icons => Fonts.Icons;

    public static TextBlock Glyph(string glyph, double size, IBrush? foreground = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = foreground ?? Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rounded square with an icon, as in front of each setting.</summary>
    public static Border IconTile(string glyph, Color color, double size = 30) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(Look.Pick(3, size * 0.27)),
        Background = Look.Pick<IBrush?>(null, new SolidColorBrush(color).ToImmutable()),
        BorderBrush = Look.Pick<IBrush?>(Line, null), BorderThickness = new Thickness(Look.Pick(1, 0)),
        Child = Glyph(glyph, size * 0.47, Look.Pick<IBrush?>(Dim, null)),
    };

    /// <summary>A round face: a picture, or initials on a color.</summary>
    public static Border Face(string initials, IBrush fill, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill, ClipToBounds = true,
        Child = new TextBlock
        {
            Text = initials, FontSize = size * 0.38, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    public static TextBlock Caption(string title) => new()
    {
        Text = title.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 0, 6),
    };

    /// <summary>The WPF app's action button: a soft button, in the accent colour when it is the one that matters.</summary>
    public static Button Action(string label, bool primary = false)
    {
        var button = new Button
        {
            Content = label, Padding = new Thickness(14, 6, 14, 6), FontSize = 12, FontWeight = FontWeight.SemiBold,
            MinHeight = 32, CornerRadius = new CornerRadius(Look.Pick(3, 7)), BorderThickness = new Thickness(1),
            Background = primary ? Accent : Card, BorderBrush = primary ? Brushes.Transparent : B(0x24FFFFFF),
            Foreground = primary ? OnAccent : Brushes.White,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalContentAlignment = VerticalAlignment.Center,
        };
        // The theme repaints a button under the mouse; ours only dims, as in the WPF app.
        foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" })
        {
            button.Resources["ButtonBackground" + state] = button.Background;
            button.Resources["ButtonForeground" + state] = button.Foreground;
            button.Resources["ButtonBorderBrush" + state] = button.BorderBrush;
        }
        button.PointerEntered += (_, _) => button.Opacity = 0.82;
        button.PointerExited += (_, _) => button.Opacity = 1;
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == InputElement.IsEnabledProperty) button.Opacity = button.IsEnabled ? 1 : 0.4;
            if (e.Property == TemplatedControl.BackgroundProperty)
                foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" }) button.Resources["ButtonBackground" + state] = button.Background;
        };
        return button;
    }

    /// <summary>A dark text box with a hint while it's empty.</summary>
    public static TextBox Field(string hint, bool multiline = false)
    {
        IBrush fill = B(0x1FFFFFFF), border = B(0x24FFFFFF);
        var box = new TextBox
        {
            Foreground = Brushes.White, CaretBrush = Brushes.White, SelectionBrush = Look.Pick(B(0xFF5A5C61), Accent), Background = fill, BorderBrush = border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Look.Pick(3, 8)), Padding = new Thickness(10, 7), FontSize = 13,
            Watermark = hint, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = 0,
        };
        foreach (string state in new[] { "PointerOver", "Focused" })
        {
            box.Resources["TextControlBackground" + state] = fill;
            box.Resources["TextControlForeground" + state] = Brushes.White;
            box.Resources["TextControlBorderBrush" + state] = state == "Focused" ? Accent : border;
            box.Resources["TextControlPlaceholderForeground" + state] = Faint;
        }
        box.Resources["TextControlPlaceholderForeground"] = Faint;
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        return box;
    }

    public static ScrollViewer Scroller(Control content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(0, 0, 6, 0),
    };
}
