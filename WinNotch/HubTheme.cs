using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinNotch;

/// <summary>The look of the Holm window (settings, friends, chat, achievements): dark, rounded, like the notch.</summary>
internal static class HubTheme
{
    public static readonly Brush Panel = Frozen(Color.FromArgb(0xFA, 0x1E, 0x1E, 0x20));
    public static readonly Brush Sidebar = Frozen(Color.FromArgb(0xFF, 0x17, 0x17, 0x19));
    public static readonly Brush Edge = Frozen(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Card = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    public static readonly Brush CardStrong = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Hover = Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Selected = Frozen(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Line = Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Dim = Frozen(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Faint = Frozen(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
    public static readonly Brush Accent = Frozen(Color.FromRgb(0x0A, 0x84, 0xFF));
    public static readonly Brush Green = Frozen(Color.FromRgb(0x30, 0xD1, 0x58));
    public static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x45, 0x3A));
    public static readonly Brush Gold = Frozen(Color.FromRgb(0xFF, 0xC4, 0x3D));
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Text = new("Segoe UI Variable Text, Segoe UI");

    public static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static TextBlock Glyph(string glyph, double size, Brush? foreground = null) => new()
    {
        Text = glyph, FontFamily = Icons, FontSize = size, Foreground = foreground ?? Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A rounded square with an icon, as in front of each setting.</summary>
    public static Border IconTile(string glyph, Color color, double size = 30) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.27), Background = Frozen(color),
        Child = Glyph(glyph, size * 0.47),
    };

    /// <summary>A round face: a picture, or initials on a color.</summary>
    public static Border Face(string initials, Brush fill, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = fill,
        Child = new TextBlock
        {
            Text = initials, FontSize = size * 0.38, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, FontFamily = Text,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    public static TextBlock Caption(string title) => new()
    {
        Text = title.ToUpperInvariant(), Foreground = Dim, FontSize = 11, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(4, 0, 0, 6),
    };

    public static Button Action(string label, bool primary = false)
    {
        var button = new Button { Style = ActionButtonStyle(), Content = label, Padding = new Thickness(14, 6, 14, 6) };
        if (primary) { button.Background = Accent; button.BorderBrush = Brushes.Transparent; }
        return button;
    }

    public static Style ActionButtonStyle() => (Style)System.Windows.Markup.XamlReader.Parse(@"
        <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
               xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
            <Setter Property='Foreground' Value='White'/>
            <Setter Property='Background' Value='#14FFFFFF'/>
            <Setter Property='BorderBrush' Value='#24FFFFFF'/>
            <Setter Property='FontSize' Value='12'/>
            <Setter Property='FontWeight' Value='SemiBold'/>
            <Setter Property='MinHeight' Value='32'/>
            <Setter Property='Cursor' Value='Hand'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='Button'>
                        <Border x:Name='Surface' Background='{TemplateBinding Background}'
                                BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='7'
                                Padding='{TemplateBinding Padding}'>
                            <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property='IsMouseOver' Value='True'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.82'/>
                            </Trigger>
                            <Trigger Property='IsPressed' Value='True'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.65'/>
                            </Trigger>
                            <Trigger Property='IsKeyboardFocused' Value='True'>
                                <Setter TargetName='Surface' Property='BorderBrush' Value='#B3FFFFFF'/>
                            </Trigger>
                            <Trigger Property='IsEnabled' Value='False'>
                                <Setter TargetName='Surface' Property='Opacity' Value='0.4'/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>");

    /// <summary>A thin, dark scroll bar: the standard one is a light gray block in this dark window.</summary>
    public static Style ThinScrollBar() => (Style)System.Windows.Markup.XamlReader.Parse(@"
        <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
               xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
            <Setter Property='Width' Value='6'/>
            <Setter Property='MinWidth' Value='6'/>
            <Setter Property='Template'>
                <Setter.Value>
                    <ControlTemplate TargetType='ScrollBar'>
                        <Track x:Name='PART_Track' IsDirectionReversed='True'>
                            <Track.Thumb>
                                <Thumb>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='Thumb'>
                                            <Border Background='#55FFFFFF' CornerRadius='3'/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>");

    public static ScrollViewer Scroller(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Resources = { { typeof(ScrollBar), ThinScrollBar() } }, Padding = new Thickness(0, 0, 6, 0), Focusable = false,
    };

    /// <summary>A dark text box with a hint while it's empty. The box is returned; the hint sits in <paramref name="host"/>.</summary>
    public static TextBox Field(string hint, out Grid host, bool multiline = false)
    {
        var box = new TextBox
        {
            Foreground = Brushes.White, CaretBrush = Brushes.White, Background = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
            BorderBrush = Frozen(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 6, 6, 6), FontSize = 13, Template = FieldTemplate(),
            AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        };
        var placeholder = new TextBlock
        {
            Text = hint, Foreground = Faint, IsHitTestVisible = false,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 13,
        };
        host = new Grid();
        host.Children.Add(box);
        host.Children.Add(placeholder);
        box.TextChanged += (_, _) => placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return box;
    }

    static ControlTemplate FieldTemplate() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
        <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                         xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'>
            <Border x:Name='Box' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
                    BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8'>
                <ScrollViewer x:Name='PART_ContentHost' Margin='0,6' VerticalAlignment='Center'/>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property='IsKeyboardFocused' Value='True'>
                    <Setter TargetName='Box' Property='BorderBrush' Value='#FF0A84FF'/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>");
}

/// <summary>A page of the Holm window other than the switches (friends, chat, achievements). Told when it goes away.</summary>
internal abstract class HubView : Border
{
    /// <summary>The page is no longer shown: stop listening for events.</summary>
    public virtual void Detach() { }
}

/// <summary>An entry in the Holm window's sidebar.</summary>
/// <param name="Build">Makes the page, each time it's opened.</param>
/// <param name="Badge">A count to show beside the entry (unread messages), or 0.</param>
internal sealed record HubPage(string Key, string Glyph, string Title, string Subtitle, Func<HubView> Build, Func<int>? Badge = null);
