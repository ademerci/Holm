using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinNotch;

/// <summary>Achievements: how many you have, then a card for each with its progress.</summary>
internal sealed class AchievementsPage : HubView
{
    public AchievementsPage(AchievementBook book)
    {
        var content = new StackPanel();
        int unlocked = book.UnlockedCount, total = AchievementBook.All.Count;

        // The summary: a trophy, the count and a bar.
        var summary = new Grid();
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        summary.Children.Add(new Border
        {
            Width = 56, Height = 56, CornerRadius = new CornerRadius(16), Margin = new Thickness(0, 0, 16, 0),
            Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD6, 0x6B), Color.FromRgb(0xFF, 0x9F, 0x0A), 90),
            Child = HubTheme.Glyph("", 26),
        });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = $"{unlocked} of {total} unlocked", FontSize = 18, FontWeight = FontWeights.SemiBold });
        words.Children.Add(new TextBlock
        {
            Text = unlocked == total ? "You have them all." : "Your friends see how many you have.",
            Foreground = HubTheme.Dim, FontSize = 12, Margin = new Thickness(0, 2, 0, 8),
        });
        words.Children.Add(Bar((double)unlocked / total, HubTheme.Gold, 8));
        Grid.SetColumn(words, 1);
        summary.Children.Add(words);
        content.Children.Add(new Border { Background = HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = summary });

        var cards = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
        // Unlocked first (newest first), then the closest to unlocking.
        var order = AchievementBook.All
            .OrderByDescending(book.IsUnlocked)
            .ThenByDescending(a => book.UnlockedAt(a) ?? DateTimeOffset.MinValue)
            .ThenByDescending(a => Math.Min(1, (double)book.Value(a.Counter) / a.Goal));
        foreach (var achievement in order) cards.Children.Add(Card(book, achievement));
        content.Children.Add(cards);
        Child = HubTheme.Scroller(content);
    }

    static FrameworkElement Card(AchievementBook book, Achievement achievement)
    {
        bool done = book.IsUnlocked(achievement);
        long value = Math.Min(book.Value(achievement.Counter), achievement.Goal);
        var stack = new StackPanel();
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = HubTheme.IconTile(achievement.Glyph, done ? achievement.Color : Color.FromRgb(0x3A, 0x3A, 0x3C), 42);
        tile.Opacity = done ? 1 : 0.8;
        if (!done) ((TextBlock)tile.Child).Foreground = HubTheme.Faint;
        tile.Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(tile);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = achievement.Title, FontWeight = FontWeights.SemiBold, Foreground = done ? Brushes.White : HubTheme.Dim, TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(new TextBlock { Text = achievement.Description, FontSize = 12, Foreground = HubTheme.Dim, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(words, 1);
        top.Children.Add(words);
        stack.Children.Add(top);

        var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        if (done)
        {
            var at = book.UnlockedAt(achievement)!.Value.LocalDateTime;
            foot.Children.Add(new TextBlock
            {
                Text = "Unlocked " + at.ToString("d MMM yyyy", CultureInfo.CurrentCulture), FontSize = 11,
                Foreground = HubTheme.Frozen(achievement.Color), FontWeight = FontWeights.SemiBold,
            });
        }
        else
        {
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bar = Bar((double)value / achievement.Goal, HubTheme.Frozen(achievement.Color), 6);
            bar.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(bar);
            var count = new TextBlock { Text = $"{value}/{achievement.Goal}", FontSize = 11, Foreground = HubTheme.Faint, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(count, 1);
            foot.Children.Add(count);
        }
        stack.Children.Add(foot);

        return new Border
        {
            Background = done ? HubTheme.CardStrong : HubTheme.Card, CornerRadius = new CornerRadius(12), Padding = new Thickness(14),
            Width = 203, Margin = new Thickness(0, 0, 10, 10), Child = stack,
            BorderBrush = done ? HubTheme.Frozen(Color.FromArgb(0x55, achievement.Color.R, achievement.Color.G, achievement.Color.B)) : Brushes.Transparent,
            BorderThickness = new Thickness(1),
        };
    }

    /// <summary>A rounded progress bar.</summary>
    static FrameworkElement Bar(double share, Brush fill, double height)
    {
        var grid = new Grid { Height = height };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(share, 0, 1), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - Math.Clamp(share, 0, 1), GridUnitType.Star) });
        var track = new Border { Background = HubTheme.Line, CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        grid.Children.Add(track);
        if (share > 0) grid.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
        return grid;
    }
}
