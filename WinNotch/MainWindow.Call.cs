using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinNotch.Notify;

namespace WinNotch;

/// <summary>
/// A call as a live activity: while Discord, Teams, Zoom, WhatsApp or a call in the browser uses the microphone,
/// the notch shows the app and how long the call runs, with a red mark when the microphone is muted; open, it has
/// a button to mute it. A pop-out says when the call ended.
/// </summary>
public partial class MainWindow
{
    SensorUse? _call;
    DispatcherTimer _callTick = null!;
    string? _shownCallTime;

    void InitCall() => _callTick = MakeTimer(1000, UpdateCallTime);

    /// <summary>Called whenever the apps using the microphone or its mute change.</summary>
    void ShowCall()
    {
        var call = _settings.Calls ? Calls.Current(AllSensors) : null;
        if (call?.Key != _call?.Key)
        {
            var ended = _call;
            _call = call;
            _shownCallTime = null;
            if (call != null)
            {
                Diag.Log($"Call started: {call.Name}");
                CallLogoHost.Child = CallLogo(call, 20);
                CallRowLogoHost.Child = CallLogo(call, 28);
                CallRowTitle.Text = Calls.Title(call);
            }
            else if (ended != null && _settings.Calls) // not when you just switched calls off
            {
                Diag.Log("Call ended");
                ShowPeek("", "Call ended", FormatTime(DateTime.UtcNow - ended.Since), null, false, DimBrush);
            }
            CallRow.Visibility = call != null ? Visibility.Visible : Visibility.Collapsed;
            ApplyState();
        }
        if (_call == null)
        {
            _callTick.Stop();
            return;
        }

        bool muted = _mic.TryGet(out _, out bool m) && m;
        CallMutedIcon.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;
        CallMuteBtn.Content = muted ? "Unmute" : "Mute";
        if (muted) CallMuteBtn.Background = RedBrush;
        else CallMuteBtn.ClearValue(BackgroundProperty); // the style's own
        CallMuteBtn.ToolTip = muted ? "Your microphone is muted for every app" : "Mute your microphone for every app";
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
            CallTime.Text = CallRowTime.Text = text;
        }
        _callTick.Interval = TimeSpan.FromMilliseconds(1000 - elapsed.TotalMilliseconds % 1000 + 15);
        if (!_callTick.IsEnabled) _callTick.Start();
    }

    /// <summary>The messenger's own icon when Holm knows the app (Discord, WhatsApp, …), a green phone otherwise.</summary>
    FrameworkElement CallLogo(SensorUse call, double size)
    {
        if ((Messengers.ForProcess(call.Key) ?? Messengers.ForApp(call.Key)) is { } messenger)
            return AppLogo(messenger.Key, size);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = GreenBrush,
            Child = new TextBlock
            {
                Text = "", FontFamily = IconFont, FontSize = size * 0.5, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    void CallMute_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        bool muted = _mic.TryGet(out _, out bool m) && m;
        _mic.SetMute(!muted);
        Diag.Log(muted ? "Microphone unmuted" : "Microphone muted");
        ShowPrivacy();
    }

    void MenuCalls_Click(object sender, RoutedEventArgs e)
    {
        _settings.Calls = MenuCalls.IsChecked;
        _settings.Save();
        ShowPrivacy();
    }
}
