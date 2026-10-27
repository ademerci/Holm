using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Notify;

namespace WinNotch;

/// <summary>
/// Quiet hours: from one time of day to another, messages don't pop out of the notch (they still count on the
/// badge), except from the apps and people you let through.
/// </summary>
public partial class MainWindow
{
    static readonly string[] QuietStarts = { "20:00", "21:00", "22:00", "23:00", "00:00" };
    static readonly string[] QuietEnds = { "05:00", "06:00", "07:00", "08:00", "09:00" };

    bool QuietNow => _settings.QuietHours && QuietHours.Parse(_settings.QuietFrom) is { } from && QuietHours.Parse(_settings.QuietUntil) is { } until
                     && QuietHours.IsQuiet(from, until, DateTime.Now);

    /// <summary>Whether a message that just came in stays in the badge instead of popping out.</summary>
    bool Silenced(ChatMessage message)
        => QuietHours.Silences(_settings.QuietHours, _settings.QuietFrom, _settings.QuietUntil, DateTime.Now, message.App, message.Title, _settings.QuietVips);

    /// <summary>A line in the open notch while it is quiet, so a missing pop-out never looks like a fault.</summary>
    void ShowQuietNote()
    {
        bool quiet = QuietNow;
        QuietNote.Visibility = quiet ? Visibility.Visible : Visibility.Collapsed;
        if (quiet) QuietNote.Text = _settings.QuietVips.Count > 0 ? $"Quiet until {_settings.QuietUntil} · VIPs still pop out" : $"Quiet until {_settings.QuietUntil}";
    }

    void FillQuietMenu()
    {
        MenuQuiet.Items.Clear();
        var on = new MenuItem { Header = "Quiet hours", IsCheckable = true, IsChecked = _settings.QuietHours, StaysOpenOnClick = true };
        on.Click += (_, _) => { _settings.QuietHours = on.IsChecked; SaveQuiet(); };
        MenuQuiet.Items.Add(on);
        MenuQuiet.Items.Add(TimeChoices("From", QuietStarts, () => _settings.QuietFrom, t => _settings.QuietFrom = t));
        MenuQuiet.Items.Add(TimeChoices("Until", QuietEnds, () => _settings.QuietUntil, t => _settings.QuietUntil = t));
        MenuQuiet.Items.Add(new Separator());

        MenuQuiet.Items.Add(new MenuItem { Header = "Still pop out:", IsEnabled = false });
        foreach (var app in Messengers.All) MenuQuiet.Items.Add(VipChoice(app.Name, QuietHours.AppPrefix + app.Key));
        // Who wrote lately, and whoever was let through before, so they can be taken off again.
        var people = QuietHours.RecentPeople(_chat.Messages.Where(m => !m.CountOnly).Select(m => (m.Title, m.Time)));
        foreach (var vip in _settings.QuietVips.Where(v => v.StartsWith(QuietHours.PersonPrefix, StringComparison.Ordinal)))
        {
            string name = vip[QuietHours.PersonPrefix.Length..];
            if (!people.Contains(name, StringComparer.OrdinalIgnoreCase)) people.Add(name);
        }
        if (people.Count == 0) return;
        MenuQuiet.Items.Add(new Separator());
        foreach (var name in people) MenuQuiet.Items.Add(VipChoice(name, QuietHours.PersonPrefix + name));
    }

    MenuItem TimeChoices(string label, string[] times, Func<string?> get, Action<string> set)
    {
        var menu = new MenuItem { Header = $"{label} {get()}" };
        foreach (var time in times)
        {
            var item = new MenuItem { Header = time, IsCheckable = true, IsChecked = get() == time };
            item.Click += (_, _) => { set(time); SaveQuiet(); };
            menu.Items.Add(item);
        }
        return menu;
    }

    MenuItem VipChoice(string header, string vip)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, StaysOpenOnClick = true,
                                  IsChecked = _settings.QuietVips.Contains(vip, StringComparer.OrdinalIgnoreCase) };
        item.Click += (_, _) =>
        {
            _settings.QuietVips.RemoveAll(v => string.Equals(v, vip, StringComparison.OrdinalIgnoreCase));
            if (item.IsChecked) _settings.QuietVips.Add(vip);
            SaveQuiet();
        };
        return item;
    }

    void SaveQuiet()
    {
        _settings.Save();
        ShowQuietNote();
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }
}
