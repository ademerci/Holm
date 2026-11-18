using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Holm.App.Controls;

namespace Holm.App;

/// <summary>The look of the Holm window (settings, friends, chat, achievements): dark and rounded, or black and square in the instrument look, like the notch (the WPF app's HubTheme).</summary>
internal static class HubTheme
{
    static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();

    // The design's palette (Look.Current): Classic is dark and rounded, Instrument black with grey lines, the others their own.
    static readonly WinNotch.Design D = Look.Current;
    public static readonly IBrush Panel = Look.Aero ? Look.VerticalGradient((0xFFF1FCFF, 0), (D.Panel, 0.56), (0xFFA5DFFF, 1)) : B(D.Panel),
        Sidebar = Look.Aero ? Look.VerticalGradient((0xFF61C5FD, 0), (0xFFCBF8FF, 0.29), (D.Sidebar, 0.52), (0xFF44C7EE, 1)) : B(D.Sidebar),
        Edge = B(D.WindowEdgeColor),
        Card = Look.Aero ? Look.VerticalGradient((0xFFFFFFFF, 0), (D.CardColor, 0.48), (0xFFDBF1FC, 1)) : B(D.CardColor),
        CardStrong = B(D.CardStrongColor), Hover = B(D.HoverColor),
        Selected = Look.Aero ? Look.VerticalGradient((0xFFF9FFE3, 0), (0xFFDEF9AD, 0.46),
            (D.SelectedColor, 0.49), (0xFF9CDD57, 1)) : B(D.SelectedColor),
        Line = B(D.LineColor),
        Dim = B(D.DimColor), Faint = B(D.FaintColor), Accent = Look.Accent, Green = B(0xFF30D158), Red = B(0xFFFF453A), Gold = B(0xFFFFC43D);
    public static readonly IBrush ActionFill = Look.Aero
        ? Look.VerticalGradient((Mix(D.Accent, 0xFFFFFFFF, 0.16), 0), (Mix(D.Accent, 0xFFFFFFFF, 0.06), 0.43),
            (Mix(D.Accent, 0xFF000000, 0.18), 0.46), (D.Accent, 1))
        : Accent;
    public static readonly IBrush ActionSurface = Look.Aero
        ? Look.VerticalGradient((0xFFFFFFFF, 0), (0xFFE1FAFF, 0.43), (0xFFA9DEF7, 0.46), (0xFFDAF8FF, 1))
        : Look.Prism ? PrismGlass.FloatingSurface(D.CardColor, D.Ink) : Card;
    /// <summary>Text and glyphs on <see cref="Accent"/>.</summary>
    public static readonly IBrush OnAccent = Look.OnAccent;
    /// <summary>A surface raised over the panel (dialogs, popups), opaque.</summary>
    public static readonly IBrush Raised = B(Mix(D.Panel | 0xFF000000, D.Ink, 0.08));
    /// <summary>Rounded corners of rows and buttons, from the design's window corners.</summary>
    public static double Round(double classic) => Math.Min(classic, D.Corners * classic / 14);

    static uint Mix(uint a, uint b, double t)
    {
        static uint C(uint x, int shift) => x >> shift & 0xFF;
        uint M(int s) => (uint)Math.Round(C(a, s) + (C(b, s) - (double)C(a, s)) * t);
        return 0xFF000000 | M(16) << 16 | M(8) << 8 | M(0);
    }

    public static FontFamily Icons => Fonts.Icons;

    public static TextBlock Glyph(string glyph, double size, IBrush? foreground = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = foreground ?? Look.Ink,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rounded square with an icon, as in front of each setting.</summary>
    public static Border IconTile(string glyph, Color color, double size = 30) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(Look.Pick(3, Round(size * 0.27))),
        Background = Look.Aero ? Look.VerticalGradient((Mix(color.ToUInt32(), 0xFFFFFFFF, 0.22), 0), (color.ToUInt32(), 0.46),
            (Mix(color.ToUInt32(), 0xFF000000, 0.22), 0.48), (color.ToUInt32(), 1)) : Look.Pick<IBrush?>(null, new SolidColorBrush(color).ToImmutable()),
        BorderBrush = Look.Aero ? B(0x700B5276) : Look.Pick<IBrush?>(Line, null), BorderThickness = new Thickness(Look.Aero ? 1 : Look.Pick(1, 0)),
        Child = Glyph(glyph, size * 0.47, Look.Pick(Dim, Brushes.White)),
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
            MinHeight = 32, CornerRadius = new CornerRadius(Look.Prism ? 16 : Round(7)), BorderThickness = new Thickness(1),
            Background = primary ? ActionFill : ActionSurface, BorderBrush = Look.Aero ? Edge : primary ? Brushes.Transparent : Look.InkA(0x24),
            Foreground = primary ? OnAccent : Look.Ink,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalContentAlignment = VerticalAlignment.Center,
        };
        // The theme repaints a button under the mouse; ours only dims, as in the WPF app.
        foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" })
        {
            button.Resources["ButtonBackground" + state] = button.Background;
            button.Resources["ButtonForeground" + state] = button.Foreground;
            button.Resources["ButtonBorderBrush" + state] = button.BorderBrush;
        }
        button.PointerEntered += (_, _) => { if (button.IsEnabled) button.Opacity = 0.82; };
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
        IBrush fill = Look.Aero ? Brushes.White : Look.InkA(0x1F), border = Look.Aero ? Line : Look.InkA(0x24);
        var box = new TextBox
        {
            Foreground = Look.Ink, CaretBrush = Look.Ink, SelectionBrush = Look.Pick(B(0xFF5A5C61), Accent), Background = fill, BorderBrush = border,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Round(8)), Padding = new Thickness(10, 7), FontSize = 13,
            Watermark = hint, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = 0,
        };
        foreach (string state in new[] { "PointerOver", "Focused" })
        {
            box.Resources["TextControlBackground" + state] = fill;
            box.Resources["TextControlForeground" + state] = Look.Ink;
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
