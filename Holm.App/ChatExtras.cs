using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WinNotch.Sharing;

namespace Holm.App;

/// <summary>
/// The extras of the chat, as on Instagram (the WPF app's ChatExtras.cs): the reaction bar over a message (quick
/// reactions and “+” for more), the emoji picker where the quick reactions are changed, and the GIF picker.
/// </summary>
internal static class ChatExtras
{
    static IBrush B(uint argb) => new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
    static readonly IBrush PopupBack = HubTheme.Raised, PopupEdge = Look.InkA(0x30);

    static Flyout MakeFlyout(Control content, double cornerRadius = 22)
    {
        var flyout = new Flyout
        {
            Placement = PlacementMode.Top,
            ShowMode = FlyoutShowMode.Transient,
            Content = new Border
            {
                Background = PopupBack,
                BorderBrush = PopupEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(cornerRadius),
                Padding = new Thickness(6),
                Margin = new Thickness(8),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 18, Color = Color.FromArgb(0x73, 0, 0, 0) }),
                Child = content,
            },
        };
        flyout.FlyoutPresenterClasses.Add("bare");
        return flyout;
    }

    static Border EmojiButton(string emoji, double size, bool selected, Action click)
    {
        var scale = new ScaleTransform(1, 1);
        scale.Transitions = new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new BackEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(180), Easing = new BackEaseOut() },
        };
        var button = new Border
        {
            Width = size + 14, Height = size + 14, CornerRadius = new CornerRadius((size + 14) / 2),
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = selected ? HubTheme.Selected : Brushes.Transparent,
            RenderTransform = scale,
            RenderTransformOrigin = RelativePoint.Center,
            Child = new TextBlock
            {
                Text = emoji, FontSize = size,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
            },
        };
        button.PointerEntered += (_, _) =>
        {
            scale.ScaleX = scale.ScaleY = 1.22;
            if (!selected) button.Background = HubTheme.Hover;
        };
        button.PointerExited += (_, _) =>
        {
            scale.ScaleX = scale.ScaleY = 1.0;
            if (!selected) button.Background = Brushes.Transparent;
        };
        button.PointerReleased += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    /// <summary>
    /// The reaction bar over a message: the quick reactions, the one already given marked (a click takes it away), and
    /// “+” for every emoji.
    /// </summary>
    public static void ShowReactionBar(Control target, SocialContext social, string? mine, Action<string?> react)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        Flyout? flyout = null;
        foreach (string emoji in social.QuickReactions())
        {
            string e = emoji;
            row.Children.Add(EmojiButton(e, 22, e == mine, () => { flyout!.Hide(); react(e == mine ? null : e); }));
        }
        var more = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Cursor = new Cursor(StandardCursorType.Hand),
            Background = HubTheme.Card, Margin = new Thickness(4, 0, 0, 0),
            Child = HubTheme.Glyph("\uE710", 14),
        };
        ToolTip.SetTip(more, "More emoji");
        more.PointerEntered += (_, _) => more.Background = HubTheme.CardStrong;
        more.PointerExited += (_, _) => more.Background = HubTheme.Card;
        more.PointerReleased += (_, e) =>
        {
            e.Handled = true;
            flyout!.Hide();
            ShowEmojiPicker(target, social, mine, react);
        };
        row.Children.Add(more);
        flyout = MakeFlyout(row);
        flyout.ShowAt(target);
    }

    /// <summary>
    /// Every emoji to react with, and “Customize reactions” to change the quick reactions: pick a slot, then the emoji
    /// for it, as on Instagram.
    /// </summary>
    public static void ShowEmojiPicker(Control target, SocialContext social, string? mine, Action<string?> react)
    {
        var quick = social.QuickReactions().ToList();
        int slot = -1;
        bool customizing = false;
        var layout = new StackPanel { Width = 330, Margin = new Thickness(6) };
        var title = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 2, 4, 8) };
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
        var scroller = HubTheme.Scroller(grid);
        scroller.MaxHeight = 220;
        layout.Children.Add(scroller);
        layout.Children.Add(footer);
        var flyout = MakeFlyout(layout, cornerRadius: 16);

        void Show()
        {
            title.Text = customizing
                ? slot < 0 ? "Tap a reaction to change it" : "Choose its new emoji"
                : "React";
            customize.Content = customizing ? "Done" : "Customize reactions";
            reset.IsVisible = customizing;
            slots.IsVisible = customizing;
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
                        flyout.Hide();
                        react(e == mine ? null : e);
                        return;
                    }
                    if (slot < 0) return;
                    int already = quick.IndexOf(e);
                    if (already >= 0) quick[already] = quick[slot];
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
        flyout.ShowAt(target);
    }

    /// <summary>
    /// The GIF picker: search GIPHY (with a key), or pick one sent lately. A GIF link pasted into the message box goes
    /// as a GIF too, key or not.
    /// </summary>
    public static void ShowGifPicker(Control target, SocialContext social, Action<string> send)
    {
        var layout = new Grid { Width = 380, Height = 380, Margin = new Thickness(4), RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var search = HubTheme.Field("Search GIPHY");
        layout.Children.Add(search);
        var results = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var scroll = HubTheme.Scroller(results);
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);
        var footer = new TextBlock
        {
            FontSize = 10.5, Foreground = HubTheme.Faint, Margin = new Thickness(2, 6, 2, 0), TextWrapping = TextWrapping.Wrap,
            Text = "Powered by GIPHY · Tip: paste a GIF link into the message to send it.",
        };
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        var flyout = MakeFlyout(layout, cornerRadius: 16);
        CancellationTokenSource? running = null;

        void Tile(string preview, string url, double w, double h)
        {
            const double width = 117;
            double height = Math.Clamp(width * (h / Math.Max(1, w)), 60, 160);
            var view = GifPlayer.View(preview, width, height, 8, social.Animate(), maxSide: 160, hoverOnly: true, maxFrames: 48);
            var tile = new Border { Margin = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand), Child = view, CornerRadius = new CornerRadius(8) };
            tile.PointerEntered += (_, _) => tile.Opacity = 0.8;
            tile.PointerExited += (_, _) => tile.Opacity = 1;
            tile.PointerReleased += (_, e) => { e.Handled = true; flyout.Hide(); send(url); };
            results.Children.Add(tile);
        }

        void Note(string text)
            => results.Children.Add(new TextBlock { Text = text, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap, Width = 360, Margin = new Thickness(4, 10, 4, 6) });

        void Recent()
        {
            var recent = social.RecentGifs();
            if (recent.Count == 0) return;
            results.Children.Add(new TextBlock { Text = "RECENT", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = HubTheme.Dim, Width = 360, Margin = new Thickness(4, 4, 4, 4) });
            foreach (string url in recent) Tile(url, url, 1, 1);
        }

        void AskForKey()
        {
            Note("To search GIFs, Holm needs a free GIPHY API key: make one at developers.giphy.com (an “API” app), then paste it here. It stays on this PC.");
            var key = HubTheme.Field("GIPHY API key");
            key.Width = 260;
            key.Margin = new Thickness(4, 0, 6, 8);
            var save = HubTheme.Action("Save", primary: true);
            save.Margin = new Thickness(0, 0, 0, 8);
            save.Click += (_, _) =>
            {
                if ((key.Text ?? "").Trim().Length == 0) return;
                social.SetGifKey(key.Text);
                _ = Load(search.Text ?? "");
            };
            results.Children.Add(key);
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
                else results.Children.Add(new TextBlock { Text = "TRENDING", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = HubTheme.Dim, Width = 360, Margin = new Thickness(4, 8, 4, 4) });
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

        var pause = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        pause.Tick += (_, _) => { pause.Stop(); _ = Load(search.Text ?? ""); };
        search.TextChanged += (_, _) => { pause.Stop(); pause.Start(); };
        flyout.Closed += (_, _) => { pause.Stop(); running?.Cancel(); };
        flyout.Opened += (_, _) => search.Focus();
        _ = Load("");
        flyout.ShowAt(target);
    }
}
