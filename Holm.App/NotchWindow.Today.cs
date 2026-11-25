using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// The open notch's home without music, as in the WPF app (MainWindow.Today.cs, with Today.cs linked): what's next
/// and the note as two cards under the clock, each opening its tool; with music, a small clock at the top left of the
/// tool row. Music starting or stopping while it's open cross-fades the player and the clock.
/// </summary>
public sealed partial class NotchWindow
{

    static readonly IBrush NoteYellow = Brush("#FFFFD60A");

    readonly Grid _todayCards = new() { Margin = new Thickness(0, 14, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,8,*") };
    readonly TextBlock _todayReminderTitle = CardTitle(), _todayReminderWhen = CardMore(), _todayNoteTitle = CardTitle(), _todayNoteMore = CardMore();
    readonly StackPanel _mediaClockHeader = new() { Width = 110, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0), IsVisible = false, Background = Brushes.Transparent, ZIndex = 1 };
    readonly TextBlock _mediaClock = new() { FontSize = 15, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _mediaDate = new() { FontSize = 10, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };
    NotchButton _todayReminder = null!, _todayNote = null!;

    static TextBlock CardTitle() => new() { FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    static TextBlock CardMore() => new() { FontSize = 11, Foreground = Dim, TextTrimming = TextTrimming.CharacterEllipsis };

    void BuildToday()
    {
        _todayReminder = Card(BellGlyph, Look.Pick(Dim, TimerOrange), _todayReminderTitle, _todayReminderWhen);
        _todayReminder.Click += () => ToggleTool(Tool.Reminders);
        _todayNote = Card("", Look.Pick(Dim, NoteYellow), _todayNoteTitle, _todayNoteMore);
        _todayNote.Click += () => ToggleTool(Tool.Notes);
        _todayCards.Children.Add(_todayReminder);
        _todayCards.Children.Add(_todayNote);
        // A section of their own, so they can move (Settings › Start page); they still show only with the clock.
        _todaySection.Children.Add(_todayCards);

        _mediaClockHeader.Children.Add(_mediaClock);
        _mediaClockHeader.Children.Add(_mediaDate);
    }

    static NotchButton Card(string glyph, IBrush color, TextBlock title, TextBlock more)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = Look.Aero ? Look.Accent : color, VerticalAlignment = VerticalAlignment.Center });
        var words = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        words.Children.Add(title);
        words.Children.Add(more);
        Grid.SetColumn(words, 1);
        content.Children.Add(words);
        var card = NotchButton.Row(content);
        if (Look.Aero)
        {
            card.Rest = HubTheme.ActionSurface;
            card.BorderBrush = HubTheme.Edge;
            card.BorderThickness = new Thickness(1);
        }
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        return card;
    }

    /// <summary>The today cards of the open notch.</summary>
    void ShowToday()
    {
        var (reminder, when) = Today.ReminderCard(_reminders.Items.FirstOrDefault(), DateTime.UtcNow);
        _todayReminderTitle.Text = reminder;
        _todayReminderWhen.Text = when;
        var (note, more) = Today.NoteCard(_settings.NoteText);
        _todayNoteTitle.Text = note;
        _todayNoteMore.Text = more;
        _todayReminder.IsVisible = _settings.Reminders;
        _todayNote.IsVisible = _settings.Notes;
        // The reminder on the left unless you swapped them; one card alone takes the whole row.
        var (first, second) = _home.IsSwapped(HomeLayout.Today) ? (_todayNote, _todayReminder) : (_todayReminder, _todayNote);
        Grid.SetColumn(first, 0);
        Grid.SetColumnSpan(first, second.IsVisible ? 1 : 3);
        Grid.SetColumn(second, first.IsVisible ? 2 : 0);
        Grid.SetColumnSpan(second, first.IsVisible ? 1 : 3);
        _todayCards.IsVisible = _settings.Reminders || _settings.Notes;
    }

    void UpdateMediaClock(DateTime now)
    {
        _mediaClock.Text = now.ToString("HH:mm");
        _mediaDate.Text = now.ToString("ddd, MMM d", CultureInfo.GetCultureInfo("en-US"));
    }

    /// <summary>Music started or stopped while open: the player and the clock cross-fade (unless animations are off).</summary>
    void FadeIn(Control panel)
    {
        if (!_settings.Animations || _state != NotchState.Expanded) { StopTween(panel, "fade"); panel.Opacity = 1; return; }
        Animate(panel, "fade", v => panel.Opacity = v, 0, 1, 220, 0, QuadOut);
        if (panel.RenderTransform is not TranslateTransform move)
        {
            if (panel.RenderTransform != null) return;
            panel.RenderTransform = move = new TranslateTransform();
        }
        Animate(panel, "fade-slide", v => move.Y = v, 10, 0, 360, 0, Bouncy);
    }
}
