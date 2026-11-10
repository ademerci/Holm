using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinNotch.Sharing;
using EmojiText = Emoji.Wpf.TextBlock;

namespace WinNotch;

/// <summary>
/// The extras of the chat, as on Instagram: the reaction bar over a message (quick reactions and “+” for more), the
/// emoji picker where the quick reactions are changed, and the GIF picker.
/// </summary>
internal static class ChatExtras
{
    static readonly Brush PopupBack = HubTheme.Frozen(Color.FromRgb(0x2C, 0x2C, 0x2E));
    static readonly Brush PopupEdge = HubTheme.Frozen(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    /// <summary>One emoji in color (WPF on its own draws emoji in black and white).</summary>
    public static FrameworkElement Emoji(string emoji, double size) => new EmojiText
    {
        Text = emoji, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
    };

    static Popup MakePopup(UIElement target, UIElement content, PlacementMode placement = PlacementMode.Top)
    {
        var popup = new Popup
        {
            PlacementTarget = target, Placement = placement, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, VerticalOffset = placement == PlacementMode.Top ? -6 : 6,
            Child = new Border
            {
                Background = PopupBack, BorderBrush = PopupEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22),
                Padding = new Thickness(6), Child = content,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 },
                Margin = new Thickness(12),
            },
        };
        return popup;
    }

    /// <summary>A round button with an emoji that grows a little under the mouse, as in Instagram's bar.</summary>
    static Border EmojiButton(string emoji, double size, bool selected, Action click)
    {
        var scale = new ScaleTransform(1, 1);
        var button = new Border
        {
            Width = size + 14, Height = size + 14, CornerRadius = new CornerRadius((size + 14) / 2), Cursor = Cursors.Hand,
            Background = selected ? HubTheme.Selected : Brushes.Transparent, Child = Emoji(emoji, size),
            RenderTransform = scale, RenderTransformOrigin = new Point(0.5, 0.5), ToolTip = null,
        };
        void Grow(double to)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
        button.MouseEnter += (_, _) => { Grow(1.22); if (!selected) button.Background = HubTheme.Hover; };
        button.MouseLeave += (_, _) => { Grow(1.0); if (!selected) button.Background = Brushes.Transparent; };
        button.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    /// <summary>
    /// The reaction bar over a message: the quick reactions, the one already given marked (a click takes it away), and
    /// “+” for every emoji.
    /// </summary>
    public static void ShowReactionBar(UIElement target, SocialContext social, string? mine, Action<string?> react)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Popup? popup = null;
        foreach (string emoji in social.QuickReactions())
        {
            string e = emoji;
            row.Children.Add(EmojiButton(e, 22, e == mine, () => { popup!.IsOpen = false; react(e == mine ? null : e); }));
        }
        var more = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Cursor = Cursors.Hand, Background = HubTheme.Card,
            Margin = new Thickness(4, 0, 0, 0), Child = HubTheme.Glyph("", 14, Brushes.White), ToolTip = "More emoji",
        };
        more.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            popup!.IsOpen = false;
            ShowEmojiPicker(target, social, mine, react);
        };
        row.Children.Add(more);
        popup = MakePopup(target, row);
        popup.IsOpen = true;
    }

    /// <summary>
    /// Every emoji to react with, and “Customize” to change the quick reactions: pick a slot, then the emoji for it,
    /// as on Instagram.
    /// </summary>
    public static void ShowEmojiPicker(UIElement target, SocialContext social, string? mine, Action<string?> react)
    {
        var quick = social.QuickReactions().ToList();
        int slot = -1;
        bool customizing = false;
        var layout = new StackPanel { Width = 330, Margin = new Thickness(6) };
        var title = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 2, 4, 8) };
        var slots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
        var grid = new WrapPanel();
        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        var customize = HubTheme.Action("Customize reactions");
        var reset = HubTheme.Action("Reset");
        reset.HorizontalAlignment = HorizontalAlignment.Left;
        customize.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Children.Add(reset);
        footer.Children.Add(customize);
        layout.Children.Add(title);
        layout.Children.Add(slots);
        layout.Children.Add(new ScrollViewer
        {
            Content = grid, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Resources = { { typeof(ScrollBar), HubTheme.ThinScrollBar() } },
        });
        layout.Children.Add(footer);
        Popup popup = MakePopup(target, layout);
        ((Border)popup.Child).CornerRadius = new CornerRadius(16);

        void Show()
        {
            title.Text = customizing
                ? slot < 0 ? "Tap a reaction to change it" : "Choose its new emoji"
                : "React";
            customize.Content = customizing ? "Done" : "Customize reactions";
            reset.Visibility = customizing ? Visibility.Visible : Visibility.Collapsed;
            slots.Visibility = customizing ? Visibility.Visible : Visibility.Collapsed;
            slots.Children.Clear();
            for (int i = 0; i < quick.Count; i++)
            {
                int index = i;
                slots.Children.Add(EmojiButton(quick[i], 22, index == slot, () => { slot = index; Show(); }));
            }
            grid.Children.Clear();
            foreach (string emoji in ChatReaction.Picker)
            {
                string e = emoji;
                grid.Children.Add(EmojiButton(e, 20, !customizing && e == mine, () =>
                {
                    if (!customizing)
                    {
                        popup.IsOpen = false;
                        react(e == mine ? null : e);
                        return;
                    }
                    if (slot < 0) return;
                    int already = quick.IndexOf(e);
                    if (already >= 0) quick[already] = quick[slot]; // no emoji twice: the two swap places
                    quick[slot] = e;
                    social.SetQuickReactions(quick);
                    slot = -1;
                    Show();
                }));
            }
        }
        customize.Click += (_, _) => { customizing = !customizing; slot = -1; Show(); };
        reset.Click += (_, _) =>
        {
            quick = ChatReaction.Defaults.ToList();
            social.SetQuickReactions(quick);
            slot = -1;
            Show();
        };
        Show();
        popup.IsOpen = true;
    }

    /// <summary>
    /// The GIF picker: search GIPHY (with a key), or pick one sent lately. A GIF link pasted into the message box goes
    /// as a GIF too, key or not.
    /// </summary>
    public static void ShowGifPicker(UIElement target, SocialContext social, Action<string> send)
    {
        var layout = new Grid { Width = 380, Height = 380, Margin = new Thickness(4) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var search = HubTheme.Field("Search GIPHY", out var searchHost);
        layout.Children.Add(searchHost);
        var results = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var scroll = new ScrollViewer
        {
            Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Resources = { { typeof(ScrollBar), HubTheme.ThinScrollBar() } },
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var footer = new TextBlock
        {
            FontSize = 10.5, Foreground = HubTheme.Faint, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap,
            Text = "Powered by GIPHY · Tip: paste a GIF link into the message to send it.",
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        Popup popup = MakePopup(target, layout);
        ((Border)popup.Child).CornerRadius = new CornerRadius(16);
        CancellationTokenSource? running = null;

        void Tile(string preview, string url, double w, double h)
        {
            const double width = 117;
            double height = Math.Clamp(width * (h / Math.Max(1, w)), 60, 160);
            var view = GifPlayer.View(preview, width, height, 8, social.Animate(), maxSide: 160);
            var tile = new Border { Margin = new Thickness(2), Cursor = Cursors.Hand, Child = view, CornerRadius = new CornerRadius(8) };
            tile.MouseEnter += (_, _) => tile.Opacity = 0.8;
            tile.MouseLeave += (_, _) => tile.Opacity = 1;
            tile.MouseLeftButtonUp += (_, e) => { e.Handled = true; popup.IsOpen = false; send(url); };
            results.Children.Add(tile);
        }

        void Note(string text)
            => results.Children.Add(new TextBlock { Text = text, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, Width = 360, Margin = new Thickness(4, 10, 4, 6) });

        void Recent()
        {
            var recent = social.RecentGifs();
            if (recent.Count == 0) return;
            results.Children.Add(new TextBlock { Text = "RECENT", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Dim, Width = 360, Margin = new Thickness(4, 4, 4, 4) });
            foreach (string url in recent) Tile(url, url, 1, 1);
        }

        void AskForKey()
        {
            Note("To search GIFs, Holm needs a free GIPHY API key: make one at developers.giphy.com (an “API” app), then paste it here. It stays on this PC.");
            var key = HubTheme.Field("GIPHY API key", out var keyHost);
            keyHost.Width = 260;
            keyHost.Margin = new Thickness(4, 0, 6, 8);
            var save = HubTheme.Action("Save", primary: true);
            save.Margin = new Thickness(0, 0, 0, 8);
            save.Click += (_, _) =>
            {
                if (key.Text.Trim().Length == 0) return;
                social.SetGifKey(key.Text);
                _ = Load(search.Text);
            };
            results.Children.Add(keyHost);
            results.Children.Add(save);
            Recent();
        }

        async Task Load(string words)
        {
            running?.Cancel();
            var cts = running = new CancellationTokenSource();
            results.Children.Clear();
            if (social.GifKey() is not { } key)
            {
                AskForKey();
                return;
            }
            if (words.Trim().Length == 0) Recent();
            try
            {
                var found = await GifSearch.SearchAsync(key, words, cts.Token);
                if (cts.IsCancellationRequested) return;
                if (words.Trim().Length > 0) results.Children.Clear();
                else results.Children.Add(new TextBlock { Text = "TRENDING", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = HubTheme.Dim, Width = 360, Margin = new Thickness(4, 8, 4, 4) });
                if (found.Count == 0) Note("No GIFs found.");
                foreach (var gif in found) Tile(gif.Preview, gif.Url, gif.Width, gif.Height);
            }
            catch (OperationCanceledException) { }
            catch (IOException ex)
            {
                if (cts.IsCancellationRequested) return;
                results.Children.Clear();
                Note(ex.Message + ".");
                if (ex.Message.Contains("key")) { social.SetGifKey(null); AskForKey(); }
                else Recent();
            }
        }

        // Search as you type, after a short pause.
        var pause = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        pause.Tick += (_, _) => { pause.Stop(); _ = Load(search.Text); };
        search.TextChanged += (_, _) => { pause.Stop(); pause.Start(); };
        popup.Closed += (_, _) => { pause.Stop(); running?.Cancel(); };
        popup.Opened += (_, _) => search.Focus();
        _ = Load("");
        popup.IsOpen = true;
    }
}
