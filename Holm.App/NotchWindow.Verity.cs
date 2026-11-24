using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>A moment that shows on Verity's face (<see cref="VerityFace.Express"/>).</summary>
internal enum VerityCue { None, Surprised, Cheer, Yawn }

/// <summary>
/// Verity (Verity.cs) in the notch, once switched on: small at the right of the closed notch, hopping now and
/// then and dancing beside the music, and in the open notch in the tool row at the top, on the free stretches beside
/// its buttons (VerityLane); while music shows, the tool row is full and she dances beside the song's title instead. There a click pets her, a drag picks her up to throw, a right-click feeds her, and she
/// wanders over to the mouse. She is startled by a new message or a loud volume, cheers for an achievement, yawns late
/// in the evening and gets hungry without a treat. Her switch on Settings › General (off at first) brings her in and
/// sends her away; the first time she moves in unlocks her achievement.
/// </summary>
public sealed partial class NotchWindow
{
    const double VerityClosedRoom = 24; // the closed notch grows by her width and gap
    const double VeritySeat = 44; // her seat beside the song's title: her width and a little room to dance

    readonly VerityFace _verityIdle = new() { Width = 16, Height = 16, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, IsHitTestVisible = false };
    readonly VerityFace _verityCompact = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, IsHitTestVisible = false };
    readonly VerityLane _verityLane = new() { IsVisible = false };
    readonly DispatcherTimer _verityCalm = new() { Interval = Verity.HappyFor };
    DateTime? _pettedAt;
    float _verityHeardVolume = float.NaN;

    bool VerityHome => _settings.ShowVerity;

    void InitVerity()
    {
        // Her floor is the tool row: she keeps beside its buttons.
        PlaceVerityLane();
        _verityLane.Petted += PetVerity;
        _verityLane.Ate += () =>
        {
            _settings.VerityFedAt = DateTime.Now;
            _settings.Save();
            _verityLane.Face.Express(VerityCue.Cheer);
            ShowVerity();
        };
        _verityCalm.Tick += (_, _) => { _verityCalm.Stop(); ShowVerity(); };

        var menu = new ContextMenu();
        var feed = new MenuItem { Header = "Feed Verity" };
        feed.Click += (_, _) => _verityLane.Feed();
        var pet = new MenuItem { Header = "Pet Verity" };
        pet.Click += (_, _) => PetVerity();
        menu.Items.Add(feed);
        menu.Items.Add(pet);
        // As with the tool row's other menus: the notch stays open while it shows.
        menu.Opening += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            if (_hovered && !_typing) { _collapseDelay.Stop(); _collapseDelay.Start(); }
        };
        _verityLane.Face.ContextMenu = menu;

        // Her eyes follow the mouse over the open notch, and now and then she hops over to see.
        _expandedView.AddHandler(PointerMovedEvent, (_, e) => { if (_verityLane.Active) _verityLane.Notice(e.GetPosition(_verityLane).X); },
            RoutingStrategies.Tunnel, handledEventsToo: true);
        _expandedView.PointerExited += (_, _) => _verityLane.Notice(null);
    }

    /// <summary>Her switch on Settings › General: on, she moves in (and the first time, her achievement unlocks).</summary>
    void SwitchVerity(bool on)
    {
        _settings.ShowVerity = on;
        _settings.VerityFedAt ??= DateTime.Now; // she arrives fed
        _settings.Save();
        if (on && _achievements != null && !AppSettings.ReadOnly) _achievements.Set(AchievementBook.Verity, 1);
        Diag.Log(on ? "Verity moved in" : "Verity sent away");
        ApplyState(animate: true);
    }

    /// <summary>
    /// Her floor: the tool row, or with music a seat beside the song's title. The clock, the tool words and the weather
    /// leave no stretch of the tool row wide enough for her then, and she stood on its buttons.
    /// </summary>
    Panel VerityFloor => _mediaPanel.IsVisible ? _mediaHead : (Panel)_toolRow;

    void PlaceVerityLane()
    {
        var floor = VerityFloor;
        if (_verityLane.Parent == floor) return;
        (_verityLane.Parent as Panel)?.Children.Remove(_verityLane);
        bool seat = floor == _mediaHead;
        // Beside the title: the head's last column, a little wider than she is, at the height of the song.
        Grid.SetColumn(_verityLane, seat ? 2 : 0);
        Grid.SetColumnSpan(_verityLane, seat ? 1 : 3); // in the tool row, laid over all of its columns
        _verityLane.Width = seat ? VeritySeat : double.NaN;
        _verityLane.VerticalAlignment = seat ? VerticalAlignment.Center : VerticalAlignment.Stretch;
        floor.Children.Add(_verityLane);
    }

    void PetVerity()
    {
        _pettedAt = DateTime.Now;
        _verityLane.Face.Pet();
        _verityCalm.Stop();
        _verityCalm.Start();
        ShowVerity();
    }

    /// <summary>Something happened: whichever of her shows reacts (a start, a cheer) and hops.</summary>
    void VerityReact(VerityCue cue)
    {
        if (!VerityHome) return;
        foreach (var face in new[] { _verityIdle, _verityCompact })
        {
            if (!face.Active) continue;
            face.Express(cue);
            if (_settings.Animations) face.Hop();
        }
        if (_verityLane.Active) _verityLane.React(cue);
    }

    /// <summary>The volume turned loud (90 % or more, from below): she starts.</summary>
    void VerityHearsVolume(float level, bool muted)
    {
        bool loud = !muted && level >= 0.9f;
        if (loud && _verityHeardVolume < 0.9f) VerityReact(VerityCue.Surprised);
        _verityHeardVolume = muted ? 0 : level;
    }

    /// <summary>She shows in the closed notch too (her own switch).</summary>
    bool VerityClosed => VerityHome && _settings.VerityInClosedNotch;

    double VerityRoom(NotchState target, bool mini) => VerityClosed && !mini && target is NotchState.Idle or NotchState.Media ? VerityClosedRoom : 0;

    /// <summary>Where she shows, what she feels, and which of her is awake to blink or dance.</summary>
    void ShowVerity()
    {
        bool home = VerityHome;
        var now = DateTime.Now;
        var mood = Verity.Mood(now, _info?.IsPlaying == true, _pettedAt, _settings.VerityFedAt);
        foreach (var face in new[] { _verityIdle, _verityCompact, _verityLane.Face })
        {
            face.Mood = mood;
            face.Animate = _settings.Animations;
            face.Sleepy = Verity.Sleepy(now);
        }
        _verityLane.Mood = mood;
        _verityLane.Animate = _settings.Animations;
        bool closed = VerityClosed;
        _verityIdle.IsVisible = _verityCompact.IsVisible = closed;
        _verityIdle.Active = closed && _state == NotchState.Idle && !_mini;
        _verityCompact.Active = closed && _state == NotchState.Media;
        _verityLane.Active = home && _state == NotchState.Expanded && _verityLane.IsEffectivelyVisible;
        if (!_verityLane.Active) _verityLane.Notice(null);
    }
}
