using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinNotch.Sharing;

namespace WinNotch;

/// <summary>
/// Waving at a friend: a note that pops up on their notch with "Wave back". Nothing but your name travels, sealed
/// like every friend note, and both ways at most once every 30 seconds per friend.
/// </summary>
public partial class MainWindow
{
    void OnWaved(Friend friend)
    {
        Diag.Log("A friend waved");
        if (_busy || _taskVisible || _sharing || _installingUpdate || _hiddenForFullscreen)
        {
            ShowPeek(null, $"{friend.Name} waves at you", "", null, false, null, seconds: 4);
            return;
        }
        ShowWaveFrom(friend.Name, () => WaveAt(friend));
    }

    void ShowWaveFrom(string name, Action waveBack)
    {
        ShowTask(Initials(name), AvatarColor(name), $"{name} waves at you", "Just saying hi", null,
                 ("Wave back", () => { HideTask(); waveBack(); }), ("OK", HideTask), textIcon: true);
        _taskHide.Interval = TimeSpan.FromSeconds(12);
        _taskHide.Start();
    }

    void WaveAt(Friend friend)
    {
        if (_friends.Wave(friend)) ShowPeek(null, $"You waved at {friend.Name}", "", null, false, null);
        else ShowPeek(null, $"You just waved at {friend.Name}", $"again in {Math.Ceiling(_friends.WaveWait(friend).TotalSeconds)} s", null, false, DimBrush);
    }

    /// <summary>Friends › Wave at › (each friend), and the switch for waves coming in.</summary>
    void FillWaveMenu()
    {
        if (_settings.Waves)
        {
            var wave = new MenuItem { Header = "Wave at" };
            foreach (var friend in _friends.Friends.ToList())
            {
                var item = new MenuItem { Header = friend.Name.Replace("_", "__") };
                item.Click += (_, _) => WaveAt(friend);
                wave.Items.Add(item);
            }
            MenuFriends.Items.Add(wave);
        }
        var allow = new MenuItem { Header = "Waves from friends", IsCheckable = true, IsChecked = _settings.Waves };
        allow.Click += (_, _) =>
        {
            _settings.Waves = allow.IsChecked;
            _settings.Save();
        };
        MenuFriends.Items.Add(allow);
    }
}
