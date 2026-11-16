using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using WinNotch;
using WinNotch.Notify;

namespace Holm.App;

/// <summary>
/// A call as a live activity: while Discord, Teams, Zoom, WhatsApp or a call in the browser uses the microphone,
/// the notch shows the app and how long the call runs, with a red mark when the microphone is muted; open, it has
/// a button to mute it. A pop-out says when the call ended. And the music pauses for it and plays on after, if
/// Holm paused it (the WPF app's MainWindow.Call.cs and MainWindow.CallPause.cs; CallPause and Calls linked).
/// </summary>
public sealed partial class NotchWindow
{
    const string PhoneGlyph = "", MutedMicGlyph = "", PausedGlyph = "";

    SensorUse? _call;
    string? _shownCallTime;
    readonly DispatcherTimer _callTick = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly CallPause _callPause = new();

    readonly Grid _callView = new() { Margin = new Thickness(9, 0, 14, 0) };
    readonly Border _callLogoHost = new() { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _callMutedIcon = new()
    {
        Text = MutedMicGlyph, FontFamily = IconFont, FontSize = 12, Foreground = Brush("#FFFF453A"),
        VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), IsVisible = false,
    };
    readonly TextBlock _callTime = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brush("#FF30D158"), VerticalAlignment = VerticalAlignment.Center };

    readonly Grid _callRow = new() { Margin = new Thickness(2, 12, 2, 0), IsVisible = false, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
    readonly Border _callRowLogoHost = new() { Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _callRowTitle = new() { FontSize = 13, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock _callRowTime = new() { FontSize = 12, Foreground = Brush("#FF30D158"), Margin = new Thickness(0, 1, 0, 0) };
    readonly NotchButton _callMuteBtn = NotchButton.Pill();

    void BuildCall()
    {
        _callView.Children.Add(_callLogoHost);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_callMutedIcon);
        right.Children.Add(_callTime);
        _callView.Children.Add(right);
        _callView.Opacity = 0;
        _callView.IsHitTestVisible = false;
        _islandContent.Children.Add(_callView);

        _callRow.Children.Add(_callRowLogoHost);
        var texts = new StackPanel { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_callRowTitle);
        texts.Children.Add(_callRowTime);
        Grid.SetColumn(texts, 1);
        _callRow.Children.Add(texts);
        _callMuteBtn.VerticalAlignment = VerticalAlignment.Center;
        _callMuteBtn.Click += () =>
        {
            bool muted = _sensors.TryGetMicrophoneMute(out bool m) && m;
            _sensors.SetMicrophoneMute(!muted);
            Diag.Log(muted ? "Microphone unmuted" : "Microphone muted");
        };
        Grid.SetColumn(_callMuteBtn, 2);
        _callRow.Children.Add(_callMuteBtn);
        _callTick.Tick += (_, _) => UpdateCallTime();
    }

    /// <summary>Called whenever the apps using the microphone or its mute change.</summary>
    void ShowCall()
    {
        var call = _settings.Calls ? Calls.Current(AllSensors) : null;
        if (call?.Key != _call?.Key || call?.Since != _call?.Since)
        {
            var ended = _call;
            _call = call;
            _shownCallTime = null;
            if (call != null)
            {
                Diag.Log($"Call started: {call.Name}");
                _callLogoHost.Child = CallLogo(call, 20);
                _callRowLogoHost.Child = CallLogo(call, 28);
                _callRowTitle.Text = Calls.Title(call);
            }
            else if (ended != null && _settings.Calls) // not when you just switched calls off
            {
                Diag.Log("Call ended");
                ShowPeek(PhoneGlyph, "Call ended", FormatTime(DateTime.UtcNow - ended.Since), Dim);
            }
            _callRow.IsVisible = call != null;
            ApplyState(animate: true);
        }
        if (_call == null)
        {
            _callTick.Stop();
            return;
        }

        bool muted = _sensors.TryGetMicrophoneMute(out bool m) && m;
        _callMutedIcon.IsVisible = muted;
        _callMuteBtn.Label = muted ? "Unmute" : "Mute";
        _callMuteBtn.Rest = muted ? RedBrush : NotchButton.PillRest;
        ToolTip.SetTip(_callMuteBtn, muted ? "Your microphone is muted for every app" : "Mute your microphone for every app");
        UpdateCallTime();
    }

    /// <summary>How long the call runs; the next tick comes right when the shown second changes.</summary>
    void UpdateCallTime()
    {
        if (_call is not { } call)
        {
            _callTick.Stop();
            return;
        }
        var elapsed = DateTime.UtcNow - call.Since;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        string text = FormatTime(elapsed);
        if (text != _shownCallTime)
        {
            _shownCallTime = text;
            _callTime.Text = _callRowTime.Text = text;
        }
        _callTick.Interval = TimeSpan.FromMilliseconds(1000 - elapsed.TotalMilliseconds % 1000 + 15);
        if (!_callTick.IsEnabled) _callTick.Start();
    }

    /// <summary>The messenger's own icon when Holm knows the app (Discord, WhatsApp, …), a green phone otherwise.</summary>
    Control CallLogo(SensorUse call, double size)
    {
        if ((Messengers.ForProcess(call.Key) ?? Messengers.ForApp(call.Key)) is { } messenger) return AppLogo(messenger.Key, size);
        return PhoneLogo(size);
    }

    static Control PhoneLogo(double size) => new Border
    {
        Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = Brush("#FF30D158"),
        Child = new TextBlock
        {
            Text = PhoneGlyph, FontFamily = IconFont, FontSize = size * 0.5, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        },
    };

    void SetCalls(bool on)
    {
        _settings.Calls = on;
        _settings.Save();
        ShowPrivacy();
    }

    /// <summary>Whenever the call or the music changes. Works whether or not the call shows in the notch.</summary>
    void UpdateCallPause()
    {
        if (!_settings.MediaControls || !_settings.PauseForCalls) { _callPause.Reset(); return; }
        var call = Calls.Current(AllSensors);
        string player = _info?.AppId is { Length: > 0 } id ? id : _info?.App ?? "";
        _callPause.Update(call?.Key, _info?.IsPlaying == true, player, out bool pause, out bool resume);
        if (pause)
        {
            Diag.Log("Music paused for a call");
            _media.Pause();
            ShowPeek(PausedGlyph, "Music paused for the call", "", null);
        }
        else if (resume)
        {
            Diag.Log("Music resumed after the call");
            _media.Play();
        }
    }

    void SetPauseForCalls(bool on)
    {
        _settings.PauseForCalls = on;
        _settings.Save();
        if (!on) _callPause.Reset();
        else UpdateCallPause();
    }
}
