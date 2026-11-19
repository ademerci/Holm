using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Verity (Verity.cs) in the notch, once she has been found: small at the right of the closed notch, hopping now and
/// then and dancing beside the music, and in the open notch on a floor of her own, where she hops and leaps with a somersault
/// about (VerityLane) and a click pets her. Typing "verity" on Settings › General
/// finds her and unlocks her achievement; her switch there sends her away and back.
/// </summary>
public sealed partial class NotchWindow
{
    const double VerityClosedRoom = 24; // the closed notch grows by her width and gap

    readonly VerityFace _verityIdle = new() { Width = 16, Height = 16, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, IsHitTestVisible = false };
    readonly VerityFace _verityCompact = new() { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsVisible = false, IsHitTestVisible = false };
    readonly VerityLane _verityLane = new() { Margin = new Thickness(0, 4, 0, 0), IsVisible = false };
    readonly DispatcherTimer _verityCalm = new() { Interval = Verity.HappyFor };
    DateTime? _pettedAt;
    bool _verityWasFound;

    bool VerityFound => _achievements?.Value(AchievementBook.Verity) > 0;
    bool VerityHome => _settings.Verity && VerityFound;

    void InitVerity()
    {
        _verityLane.Petted += PetVerity;
        _verityCalm.Tick += (_, _) => { _verityCalm.Stop(); ShowVerity(); };
        _verityWasFound = VerityFound;
        // Found on another computer: the Holm account brings her achievement, and with it Verity.
        _achievements.Changed += () =>
        {
            if (VerityFound == _verityWasFound) return;
            _verityWasFound = VerityFound;
            ApplyState(animate: true);
        };
    }

    /// <summary>Typed "verity" on Settings › General: she moves in.</summary>
    void FindVerity()
    {
        _settings.Verity = true;
        _settings.Save();
        if (_achievements != null && !AppSettings.ReadOnly) _achievements.Set(AchievementBook.Verity, 1);
        Diag.Log("Verity found");
        ApplyState(animate: true);
    }

    void PetVerity()
    {
        _pettedAt = DateTime.Now;
        _verityLane.Face.Pet();
        _verityCalm.Stop();
        _verityCalm.Start();
        ShowVerity();
    }

    double VerityRoom(NotchState target, bool mini) => VerityHome && !mini && target is NotchState.Idle or NotchState.Media ? VerityClosedRoom : 0;

    /// <summary>Where she shows, what she feels, and which of her is awake to blink or dance.</summary>
    void ShowVerity()
    {
        bool home = VerityHome;
        var mood = Verity.Mood(DateTime.Now, _info?.IsPlaying == true, _pettedAt);
        foreach (var face in new[] { _verityIdle, _verityCompact, _verityLane.Face })
        {
            face.Mood = mood;
            face.Animate = _settings.Animations;
        }
        _verityLane.Mood = mood;
        _verityLane.Animate = _settings.Animations;
        _verityIdle.IsVisible = _verityCompact.IsVisible = home;
        _verityIdle.Active = home && _state == NotchState.Idle && !_mini;
        _verityCompact.Active = home && _state == NotchState.Media;
        _verityLane.Active = home && _state == NotchState.Expanded && _verityLane.IsVisible;
    }
}
