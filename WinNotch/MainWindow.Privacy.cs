using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinNotch;

/// <summary>
/// Like a phone's privacy dots: orange beside the notch while an app uses the microphone, green while one uses the
/// camera. The open notch says which apps and has a button that mutes the microphone for all of them.
/// </summary>
public partial class MainWindow
{
    static readonly Brush MicOrange = Frozen(Color.FromRgb(0xFF, 0x9F, 0x0A));

    readonly VolumeService _mic = VolumeService.Microphone();
    PrivacyMonitor? _privacy;
    HashSet<(Sensor, string)> _knownSensors = new();
    IReadOnlyList<SensorUse>? _demoSensors = null; // README screenshots only (set in Debug builds)

    /// <summary>Every app recording now, whatever the dot's switch says (calls show on their own).</summary>
    IReadOnlyList<SensorUse> AllSensors => _demoSensors ?? _privacy?.InUse ?? Array.Empty<SensorUse>();

    /// <summary>The apps the dot is for: none when it's switched off.</summary>
    IReadOnlyList<SensorUse> SensorsInUse => _settings.Privacy ? AllSensors : Array.Empty<SensorUse>();

    void StartPrivacy()
    {
        try
        {
            _privacy = new PrivacyMonitor(Dispatcher);
            _privacy.Changed += OnSensorsChanged;
        }
        catch (Exception ex) { Diag.Error(ex); }
        // Apps already recording when Holm starts get the dot, but no pop-out.
        _knownSensors = SensorsInUse.Select(u => (u.Sensor, u.Key)).ToHashSet();
        _mic.Changed += (_, _) => Dispatcher.InvokeAsync(ShowPrivacy);
        _mic.DefaultDeviceChanged += () => Dispatcher.InvokeAsync(() => { _mic.Bind(); ShowPrivacy(); });
        _mic.Bind();
        ShowPrivacy();
    }

    void StopPrivacy()
    {
        _privacy?.Dispose();
        _privacy = null;
        _mic.Shutdown();
    }

    void OnSensorsChanged()
    {
        var uses = SensorsInUse;
        var started = uses.Where(u => !_knownSensors.Contains((u.Sensor, u.Key))).ToList();
        _knownSensors = uses.Select(u => (u.Sensor, u.Key)).ToHashSet();
        ShowPrivacy();
        // Say once which app started (the camera first); the dot then stays while it records. A call shows itself.
        if (_call != null) started.RemoveAll(u => u.Sensor == Sensor.Microphone && u.Key == _call.Key);
        if (started.Count == 0) return;
        var first = started[0];
        ShowPeek(first.Sensor == Sensor.Camera ? "" : "", $"{first.Name} is using the {SensorWord(first.Sensor)}", "", null, false, null);
    }

    static string SensorWord(Sensor sensor) => sensor == Sensor.Camera ? "camera" : "microphone";

    /// <summary>The dot beside the closed notch, or the rows in the open one.</summary>
    void ShowPrivacy()
    {
        var uses = SensorsInUse;
        MicrophoneDot.Visibility = uses.Any(u => u.Sensor == Sensor.Microphone) ? Visibility.Visible : Visibility.Collapsed;
        CameraDot.Visibility = uses.Any(u => u.Sensor == Sensor.Camera) ? Visibility.Visible : Visibility.Collapsed;
        PrivacyDots.Visibility = uses.Count > 0 && !_expanded ? Visibility.Visible : Visibility.Collapsed;
        if (_expanded) RefreshPrivacyRows();
        ShowCall();
        // The minimized idle bar is too thin to sit beside: it grows back while the dot shows.
        if (_state == NotchState.Idle && _settings.MiniIdle)
        {
            _state = NotchState.None;
            ApplyState();
        }
    }

    void RefreshPrivacyRows()
    {
        PrivacyRows.Children.Clear();
        // The call's microphone is in the call row, with its own mute button.
        var call = _settings.Calls ? Calls.Current(AllSensors) : null;
        var uses = SensorsInUse.Where(u => call == null || u.Sensor != Sensor.Microphone || u.Key != call.Key).ToList();
        var shown = uses.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool resize = PrivacyRows.Visibility != shown;
        PrivacyRows.Visibility = shown;

        foreach (var group in uses.GroupBy(u => u.Sensor))
        {
            var sensor = group.Key;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6), MinHeight = 26 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = sensor == Sensor.Camera ? "" : "", FontFamily = IconFont, FontSize = 14,
                Foreground = sensor == Sensor.Camera ? GreenBrush : MicOrange, VerticalAlignment = VerticalAlignment.Center,
            });

            var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            text.Inlines.Add(new Run(string.Join(", ", group.Select(u => u.Name).Distinct())) { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run($" · {SensorWord(sensor)} · {Since(group.Min(u => u.Since))}") { Foreground = DimBrush });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            if (sensor == Sensor.Microphone && _mic.TryGet(out _, out bool muted))
            {
                var button = new Button
                {
                    Style = (Style)FindResource("PillButton"),
                    Content = muted ? "Unmute" : "Mute",
                    ToolTip = muted ? "The microphone is muted for every app" : "Mute the microphone for every app",
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (muted) button.Background = RedBrush;
                button.Click += (_, e) =>
                {
                    e.Handled = true;
                    _mic.SetMute(!muted);
                    Diag.Log(muted ? "Microphone unmuted" : "Microphone muted");
                    ShowPrivacy();
                };
                Grid.SetColumn(button, 2);
                row.Children.Add(button);
            }
            PrivacyRows.Children.Add(row);
        }
        if (resize && _expanded && _state == NotchState.Expanded) ApplyState();
    }

    /// <summary>"just now", "for 12 min", "for 1 h 5 min".</summary>
    static string Since(DateTime startUtc)
    {
        var span = DateTime.UtcNow - startUtc;
        if (span < TimeSpan.FromMinutes(1)) return "just now";
        int minutes = (int)span.TotalMinutes;
        return minutes < 60 ? $"for {minutes} min" : $"for {minutes / 60} h {minutes % 60} min";
    }

    void MenuPrivacy_Click(object sender, RoutedEventArgs e)
    {
        _settings.Privacy = MenuPrivacy.IsChecked;
        _settings.Save();
        _knownSensors = SensorsInUse.Select(u => (u.Sensor, u.Key)).ToHashSet();
        ShowPrivacy();
    }
}
