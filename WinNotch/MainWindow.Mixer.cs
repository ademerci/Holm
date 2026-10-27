using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinNotch;

/// <summary>
/// The volume of each app, in the open notch: a slider per app that plays sound, the icon mutes it. It follows
/// Windows' session events only while the page is open.
/// </summary>
public partial class MainWindow
{
    AppVolumes? _mixer;
    DispatcherTimer _mixerSoon = null!;
    readonly Dictionary<string, ImageSource?> _mixerIcons = new(StringComparer.OrdinalIgnoreCase);
    bool _mixerDragging, _mixerStarting;

    void InitMixer() => _mixerSoon = MakeTimer(150, () => _ = ReadMixerAsync(), oneShot: true);

    async void ShowMixerPage()
    {
        if (_mixerStarting) return;
        if (_mixer == null)
        {
            _mixerStarting = true;
            try
            {
                var mixer = await Task.Run(() => new AppVolumes()); // in the multithreaded apartment
                if (_tool != Tool.Mixer) { _ = Task.Run(mixer.Dispose); return; } // closed meanwhile
                _mixer = mixer;
                _mixer.Changed += () => Dispatcher.InvokeAsync(MixerChanged);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                Diag.Log("Mixer: " + ex.GetType().Name);
                ShowMixerRows(new List<AppVolume>());
                return;
            }
            finally { _mixerStarting = false; }
        }
        await ReadMixerAsync();
    }

    /// <summary>The page closed: no more session events.</summary>
    void HideMixer()
    {
        _mixerSoon.Stop();
        if (_mixer is not { } mixer) return;
        _mixer = null;
        _ = Task.Run(mixer.Dispose);
    }

    void MixerChanged()
    {
        if (_mixer == null || _mixerDragging) return;
        _mixerSoon.Stop(); // a burst of changes, read once
        _mixerSoon.Start();
    }

    async Task ReadMixerAsync()
    {
        if (_mixer is not { } mixer) return;
        try { ShowMixerRows(await Task.Run(mixer.Read)); }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ObjectDisposedException)
        {
            Diag.Log("Mixer: " + ex.GetType().Name);
        }
    }

    void ShowMixerRows(List<AppVolume> apps)
    {
        MixerRows.Children.Clear();
        foreach (var app in apps)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var mute = new Button { Style = (Style)FindResource("NotchButton"), Width = 34, Height = 34, ToolTip = app.Muted ? "Unmute" : "Mute", VerticalAlignment = VerticalAlignment.Center };
            mute.Content = MixerIcon(app);
            mute.Click += (_, _) => _ = Task.Run(() => _mixer?.SetMute(app.Key, !app.Muted)).ContinueWith(_ => Dispatcher.InvokeAsync(() => _ = ReadMixerAsync()));
            row.Children.Add(mute);

            var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var header = new Grid();
            header.Children.Add(new TextBlock { Text = app.Name, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 50, 0) });
            var level = new TextBlock { Text = AppVolumeList.Describe(app.Volume, app.Muted), FontSize = 11, Foreground = DimBrush, HorizontalAlignment = HorizontalAlignment.Right };
            header.Children.Add(level);
            right.Children.Add(header);
            var slider = new MiniSlider { Value = app.Volume, Margin = new Thickness(0, 4, 0, 0), Fill = app.Muted ? DimBrush : Brushes.White };
            string key = app.Key;
            slider.ValueChanging += v =>
            {
                _mixerDragging = true;
                level.Text = AppVolumeList.Describe((float)v, false);
                _ = Task.Run(() => _mixer?.SetVolume(key, (float)v));
            };
            slider.ValueCommitted += _ => _mixerDragging = false;
            right.Children.Add(slider);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            MixerRows.Children.Add(row);
        }
        MixerEmpty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_expanded && _state == NotchState.Expanded) ApplyState();
    }

    UIElement MixerIcon(AppVolume app)
    {
        if (!_mixerIcons.TryGetValue(app.ExePath, out var icon))
            _mixerIcons[app.ExePath] = icon = app.ExePath.Length > 0 ? Shell.IconOf(app.ExePath, 48) : null;
        UIElement content = icon != null
            ? new Image { Source = icon, Width = 22, Height = 22 }
            : new TextBlock { Text = "", FontFamily = IconFont, FontSize = 14, Foreground = DimBrush };
        content.Opacity = app.Muted ? 0.35 : 1;
        return content;
    }

    void MixerTool_Click(object sender, RoutedEventArgs e) => ToggleTool(Tool.Mixer);

    void MenuAppVolume_Click(object sender, RoutedEventArgs e)
    {
        _settings.AppVolume = MenuAppVolume.IsChecked;
        _settings.Save();
        RefreshToolBar();
    }
}
