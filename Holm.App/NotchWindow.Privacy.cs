using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Holm.App.Controls;
using Holm.Platform;
using WinNotch;

namespace Holm.App;

/// <summary>
/// Like a phone's privacy dots: orange inside the notch while an app uses the microphone, green while one uses the
/// camera. The open notch says which apps and has a button that mutes the microphone for all of them
/// (the WPF app's MainWindow.Privacy.cs).
/// </summary>
public sealed partial class NotchWindow
{
    const string MicGlyph = "", CameraGlyph = "";
    static readonly IBrush MicOrange = Brush("#FFFF9F0A");

    ISensorPlatform _sensors = null!;
    HashSet<(Sensor, string)> _knownSensors = new();
    readonly StackPanel _privacyDots = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    readonly Ellipse _microphoneDot = new() { Width = 7, Height = 7, Fill = MicOrange, IsVisible = false };
    readonly Ellipse _cameraDot = new() { Width = 7, Height = 7, Fill = Brush("#FF30D158"), Margin = new Thickness(4, 0, 0, 0), IsVisible = false };
    readonly Border _privacyHost = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), IsVisible = false };
    readonly StackPanel _privacyRows = new() { Margin = new Thickness(2, 12, 2, 0), IsVisible = false };
    IReadOnlyList<SensorUse> _allSensors = Array.Empty<SensorUse>();

    /// <summary>Every app recording now, whatever the dot's switch says (calls show on their own).</summary>
    IReadOnlyList<SensorUse> AllSensors => _allSensors;

    /// <summary>The apps the dot is for: none when it's switched off.</summary>
    IReadOnlyList<SensorUse> SensorsInUse => _settings.Privacy ? _allSensors : Array.Empty<SensorUse>();

    void InitPrivacy(IPlatformServices platform)
    {
        _sensors = platform.Sensors;
        _privacyDots.Children.Add(_microphoneDot);
        _privacyDots.Children.Add(_cameraDot);
        _privacyHost.Child = _privacyDots;
        // The dots keep their own space beside the content, as in the WPF app's second column.
        _islandContent.ColumnDefinitions = new ColumnDefinitions("*,Auto");
        Grid.SetColumn(_privacyHost, 1);
        _islandContent.Children.Add(_privacyHost);
        BuildCall();
    }

    void StartPrivacy()
    {
        _sensors.Changed += () => Dispatcher.UIThread.Post(OnSensorsChanged);
        _sensors.Start();
        ReadSensors();
        // Apps already recording when Holm starts get the dot, but no pop-out.
        _knownSensors = SensorsInUse.Select(u => (u.Sensor, u.Key)).ToHashSet();
        ShowPrivacy();
    }

    void ReadSensors()
        => _allSensors = _sensors.InUse.Select(u => new SensorUse(u.Camera ? Sensor.Camera : Sensor.Microphone, u.Name, u.Key, u.Since)).ToList();

    void OnSensorsChanged()
    {
        ReadSensors();
        var uses = SensorsInUse;
        var started = uses.Where(u => !_knownSensors.Contains((u.Sensor, u.Key))).ToList();
        _knownSensors = uses.Select(u => (u.Sensor, u.Key)).ToHashSet();
        ShowPrivacy();
        // Say once which app started (the camera first); the dot then stays while it records. A call shows itself.
        if (_call != null) started.RemoveAll(u => u.Sensor == Sensor.Microphone && u.Key == _call.Key);
        if (started.Count == 0) return;
        var first = started[0];
        ShowPeek(first.Sensor == Sensor.Camera ? CameraGlyph : MicGlyph, $"{first.Name} is using the {SensorWord(first.Sensor)}", "", null);
    }

    static string SensorWord(Sensor sensor) => sensor == Sensor.Camera ? "camera" : "microphone";

    /// <summary>The dots inside the closed notch, or the rows in the open one.</summary>
    void ShowPrivacy()
    {
        var uses = SensorsInUse;
        _microphoneDot.IsVisible = uses.Any(u => u.Sensor == Sensor.Microphone);
        _cameraDot.IsVisible = uses.Any(u => u.Sensor == Sensor.Camera);
        // The camera dot keeps its gap only after the microphone's.
        _cameraDot.Margin = new Thickness(_microphoneDot.IsVisible ? 4 : 0, 0, 0, 0);
        _privacyHost.IsVisible = uses.Count > 0 && !_hovered;
        if (_hovered) RefreshPrivacyRows();
        ShowCall();
        UpdateCallPause();
        // The minimized idle bar is too thin to sit beside: it grows back while the dot shows (ApplyState knows).
        if (_state == NotchState.Idle && (_mini || uses.Count > 0)) ApplyState(animate: true);
    }

    void RefreshPrivacyRows()
    {
        _privacyRows.Children.Clear();
        // The call's microphone is in the call row, with its own mute button.
        var call = _settings.Calls ? Calls.Current(AllSensors) : null;
        var uses = SensorsInUse.Where(u => call == null || u.Sensor != Sensor.Microphone || u.Key != call.Key).ToList();
        bool shown = uses.Count > 0;
        bool resize = _privacyRows.IsVisible != shown;
        _privacyRows.IsVisible = shown;

        foreach (var group in uses.GroupBy(u => u.Sensor))
        {
            var sensor = group.Key;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6), MinHeight = 26, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(new TextBlock
            {
                Text = sensor == Sensor.Camera ? CameraGlyph : MicGlyph, FontFamily = IconFont, FontSize = 14,
                Foreground = sensor == Sensor.Camera ? GreenBrush : MicOrange, VerticalAlignment = VerticalAlignment.Center,
            });
            var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            text.Inlines!.Add(new Run(string.Join(", ", group.Select(u => u.Name).Distinct())) { FontWeight = FontWeight.SemiBold });
            text.Inlines.Add(new Run($" · {SensorWord(sensor)} · {Since(group.Min(u => u.Since))}") { Foreground = Dim });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            if (sensor == Sensor.Microphone && _sensors.TryGetMicrophoneMute(out bool muted))
            {
                var button = NotchButton.Pill(muted ? RedBrush : null);
                button.Label = muted ? "Unmute" : "Mute";
                button.VerticalAlignment = VerticalAlignment.Center;
                ToolTip.SetTip(button, muted ? "The microphone is muted for every app" : "Mute the microphone for every app");
                button.Click += () =>
                {
                    _sensors.SetMicrophoneMute(!muted);
                    Diag.Log(muted ? "Microphone unmuted" : "Microphone muted");
                };
                Grid.SetColumn(button, 2);
                row.Children.Add(button);
            }
            _privacyRows.Children.Add(row);
        }
        if (resize && _hovered && _state == NotchState.Expanded) ApplyState(animate: true);
    }

    /// <summary>"just now", "for 12 min", "for 1 h 5 min".</summary>
    static string Since(DateTime startUtc)
    {
        var span = DateTime.UtcNow - startUtc;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        int minutes = (int)span.TotalMinutes;
        return minutes < 60 ? $"for {minutes} min" : $"for {minutes / 60} h {minutes % 60} min";
    }

    void SetPrivacy(bool on)
    {
        _settings.Privacy = on;
        _settings.Save();
        _knownSensors = SensorsInUse.Select(u => (u.Sensor, u.Key)).ToHashSet();
        ShowPrivacy();
    }
}
