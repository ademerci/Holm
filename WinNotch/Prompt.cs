using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinNotch;

/// <summary>A small dark window asking for one line of text. Built in code, like the other custom controls.</summary>
internal static class Prompt
{
    /// <summary>The text entered, or null if cancelled.</summary>
    public static string? Ask(Window owner, string title, string question, string initial, int maxLength)
    {
        var input = new TextBox { Text = initial, MaxLength = maxLength, Style = (Style)Application.Current.FindResource("DialogTextBox") };
        var ok = new Button { Content = "OK", IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        ok.Style = cancel.Style = (Style)Application.Current.FindResource("DialogButton");
        ok.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));

        var window = new Window
        {
            Title = title,
            Owner = owner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 420,
            ShowInTaskbar = false,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            Foreground = Brushes.White,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20, 18, 20, 18),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = question, FontSize = 13, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 12) },
                        input,
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { cancel, ok } },
                    },
                },
            },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        // No title bar, so the whole window can be dragged.
        window.MouseLeftButtonDown += (_, _) => { if (!input.IsMouseOver && !ok.IsMouseOver && !cancel.IsMouseOver) window.DragMove(); };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            input.Focus();
            Keyboard.Focus(input);
            input.SelectAll();
        };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }
}
